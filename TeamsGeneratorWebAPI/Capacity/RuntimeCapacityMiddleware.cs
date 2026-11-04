using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using TeamsGenerator.Orchestration;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Telemetry;

namespace TeamsGeneratorWebAPI.Capacity;

public sealed class RuntimeCapacityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CapacityOptions _options;
    public static readonly object GenerationEntitlementsKey = new();
    private static readonly HashSet<string> ReadPosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Teams.GetTeamsFromStorage", "Teams.ReadMatches", "Teams.ReadPlayerSwaps",
        "Teams.GetMatchday", "Teams.PostResultString", "Teams.GetScores",
        "ControlPanel.GetAllUpdates", "ControlPanel.GetAllFeedbacks", "Auth.Login"
    };
    private static readonly HashSet<string> ExpensiveActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Teams.Post", "Teams.GetAiSummary", "Teams.GetTeamsDesign", "Teams.GetAllTeamsDesign",
        "Teams.GetScoresDesign", "Teams.GetNormalizedScoresDesign", "UserPlayers.PostSharePlayers", "GroupJoins.Analyze"
    };

    public RuntimeCapacityMiddleware(RequestDelegate next, IOptions<CapacityOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public static string CallerKey(HttpContext context)
    {
        var user = RequestUserId.ResolveOptional(context.User, null);
        return !string.IsNullOrWhiteSpace(user) ? "user:" + user
            : "ip:" + (context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown");
    }

    public async Task InvokeAsync(HttpContext context, IRuntimeCapacityState state,
        IAccountEntitlementService entitlements, IUsageTelemetry telemetry)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (action == null)
        {
            await _next(context);
            return;
        }
        var operation = action.ControllerName + "." + action.ActionName;
        if (operation.Equals("Billing.Webhook", StringComparison.OrdinalIgnoreCase)
            || operation.Equals("PlayBilling.Notifications", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }
        var generation = operation.Equals("Teams.Post", StringComparison.OrdinalIgnoreCase);
        var websiteVisit = operation.Equals("WebsiteVisits.Post", StringComparison.OrdinalIgnoreCase);
        var maximumRequestBytes = websiteVisit ? Math.Min(2048, _options.MaximumRequestBytes) : _options.MaximumRequestBytes;
        var heavy = ExpensiveActions.Contains(operation);
        var mutation = !websiteVisit && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method)
            && !ReadPosts.Contains(operation) && !heavy;
        var originalCancellation = context.RequestAborted;
        var originalBody = context.Request.Body;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(originalCancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.OperationTimeoutSeconds));
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var leases = new List<IRuntimeLease>();
        var leaseRegistrations = new List<CancellationTokenRegistration>();
        MemoryStream? bufferedBody = null;
        var executingAction = false;
        context.RequestAborted = leaseCancellation.Token;
        try
        {
            // Bound unknown-length/chunked bodies too, before MVC/Newtonsoft can materialize a model.
            if (context.Request.ContentLength > maximumRequestBytes)
                throw BodyTooLarge(maximumRequestBytes);
            if (context.Request.ContentLength != 0
                && (context.Request.ContentLength.HasValue || context.Request.Headers.ContainsKey("Transfer-Encoding")
                    || !HttpMethods.IsGet(context.Request.Method)))
            {
                bufferedBody = new MemoryStream();
                var buffer = new byte[16 * 1024];
                while (true)
                {
                    var read = await originalBody.ReadAsync(buffer.AsMemory(0,
                        Math.Min(buffer.Length, maximumRequestBytes - (int)bufferedBody.Length + 1)),
                        context.RequestAborted);
                    if (read == 0) break;
                    if (bufferedBody.Length + read > maximumRequestBytes) throw BodyTooLarge(maximumRequestBytes);
                    await bufferedBody.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                }
                if (bufferedBody.Length > 0)
                {
                    bufferedBody.Position = 0;
                    using var document = await JsonDocument.ParseAsync(bufferedBody,
                        new JsonDocumentOptions { MaxDepth = RuntimePayloadGuard.MaximumJsonDepth },
                        context.RequestAborted);
                    RuntimePayloadGuard.Validate(document.RootElement, _options, heavy, operation);
                    if (generation)
                    {
                        var owner = RequestUserId.ResolveOptional(context.User, context.Request.Query["ownerId"]);
                        var entitlement = await entitlements.GetAsync(owner ?? "",
                            RequestUserId.ResolveVerifiedEmail(context.User), context.RequestAborted);
                        context.Items[GenerationEntitlementsKey] = entitlement;
                        RuntimePayloadGuard.ValidateGeneration(document.RootElement, _options,
                            entitlement.MaximumGenerationPlayers);
                    }
                    bufferedBody.Position = 0;
                }
                context.Request.Body = bufferedBody;
            }
            var caller = CallerKey(context);
            if (generation)
            {
                await state.ConsumeAsync("generation:" + caller, _options.GenerationRequestsPerMinute,
                    "generation_rate_limit", context.RequestAborted);
                await state.ConsumeAsync("generation:global", _options.GlobalGenerationRequestsPerMinute,
                    "global_generation_rate_limit", context.RequestAborted);
                await Acquire("generation:" + caller, _options.ConcurrentGenerationsPerCaller,
                    "generation_concurrency_limit");
            }
            if (mutation)
            {
                await state.ConsumeAsync("write:" + caller, _options.WritesPerMinute,
                    "write_rate_limit", context.RequestAborted);
                // Shared global backstop also protects legacy/IP callers spread across workers.
                await state.ConsumeAsync("write:global", _options.WritesPerMinute * _options.GlobalExpensiveConcurrency,
                    "global_write_rate_limit", context.RequestAborted);
            }
            if (heavy)
                await Acquire("expensive:global", _options.GlobalExpensiveConcurrency, "global_concurrency_limit");
            executingAction = true;
            using (GenerationCancellation.Enter(context.RequestAborted))
                await _next(context);
        }
        catch (CapacityException exception) when (!context.Response.HasStarted)
        {
            await Reject(exception);
        }
        catch (JsonException) when (!executingAction && !context.Response.HasStarted)
        {
            await Reject(new CapacityException("invalid_json",
                $"The request must be valid JSON with no more than {RuntimePayloadGuard.MaximumJsonDepth} nested levels.", 400));
        }
        catch (Azure.RequestFailedException exception) when (!executingAction && !context.Response.HasStarted)
        {
            context.RequestServices.GetRequiredService<ILogger<RuntimeCapacityMiddleware>>()
                .LogError(exception, "Runtime capacity admission storage is unavailable.");
            await Reject(new CapacityException("capacity_unavailable",
                "Request capacity could not be checked. Please try again shortly.", 503, retryAfterSeconds: 5));
        }
        catch (OperationCanceledException) when (!originalCancellation.IsCancellationRequested && !context.Response.HasStarted)
        {
            await Reject(new CapacityException("operation_timeout",
                "The operation could not finish in time. Please try again.", 504, retryAfterSeconds: 2));
        }
        finally
        {
            context.RequestAborted = originalCancellation;
            context.Request.Body = originalBody;
            bufferedBody?.Dispose();
            foreach (var registration in leaseRegistrations) registration.Dispose();
            // Await the real action, even if it ignores cancellation. Never abandon work and free its slot.
            try
            {
                for (var i = leases.Count - 1; i >= 1; i--) await leases[i].DisposeAsync();
            }
            finally
            {
                if (leases.Count > 0) await leases[0].DisposeAsync();
            }
        }

        async Task Acquire(string key, int slots, string code)
        {
            var lease = await state.AcquireAsync(key, slots, code, context.RequestAborted);
            leases.Add(lease);
            leaseRegistrations.Add(lease.LeaseLost.Register(() => leaseCancellation.Cancel()));
        }

        async Task Reject(CapacityException exception)
        {
            telemetry.Track("CapacityRejected", properties: new Dictionary<string, string?>
            {
                ["code"] = exception.Code, ["operation"] = operation
            });
            context.Response.StatusCode = exception.StatusCode;
            if (exception.RetryAfterSeconds.HasValue)
                context.Response.Headers["Retry-After"] = exception.RetryAfterSeconds.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new
            {
                success = false, error = exception.Message, message = exception.Message,
                code = exception.Code, limit = exception.Limit, current = exception.Current,
                retryAfterSeconds = exception.RetryAfterSeconds
            }, originalCancellation);
        }
    }

    private static CapacityException BodyTooLarge(int maximumRequestBytes) => new("request_bytes_limit",
        "The request is too large. Send fewer players or a smaller image.", 413, maximumRequestBytes);
}
