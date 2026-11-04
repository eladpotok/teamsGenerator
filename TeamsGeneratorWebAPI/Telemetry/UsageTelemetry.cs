using System.Security.Cryptography;
using System.Text;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace TeamsGeneratorWebAPI.Telemetry
{
    public interface IUsageTelemetry
    {
        void Track(
            string eventName,
            string? clientVersion = null,
            string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null);
    }

    public sealed class UsageTelemetry : IUsageTelemetry
    {
        private const string AnonymousUserNamespace = "teamify-usage-v1:";
        private readonly TelemetryClient _telemetryClient;
        private readonly string _environmentName;
        private readonly IHttpContextAccessor? _httpContext;

        public UsageTelemetry(
            TelemetryClient telemetryClient,
            IHostEnvironment environment,
            IHttpContextAccessor? httpContext = null)
        {
            _telemetryClient = telemetryClient;
            _environmentName = environment.EnvironmentName;
            _httpContext = httpContext;
        }

        public void Track(
            string eventName,
            string? clientVersion = null,
            string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null)
        {
            var telemetry = new EventTelemetry(eventName);
            var context = _httpContext?.HttpContext;
            if (UsageMetrics.IsVerificationClient(clientVersion ?? context?.Request.Headers["client_version"].ToString()))
                return;
            var authenticatedId = context == null ? null
                : Authentication.RequestUserId.ResolveOptional(context.User, null);
            var participant = properties != null
                && properties.TryGetValue("actor_type", out var actorType)
                && actorType == "participant";
            userId = participant ? null
                : !string.IsNullOrWhiteSpace(authenticatedId) ? authenticatedId : userId;
            telemetry.Properties["client_version"] =
                NormalizeValue(clientVersion ?? context?.Request.Headers["client_version"].ToString(), "unknown");
            telemetry.Properties["environment"] =
                NormalizeValue(_environmentName, "unknown");
            telemetry.Properties["schema_version"] = "2";
            telemetry.Properties["event_source"] = "backend";
            telemetry.Properties["actor_type"] =
                string.IsNullOrWhiteSpace(userId) ? "unknown" : "organizer";
            telemetry.Properties["identity_source"] = string.IsNullOrWhiteSpace(userId)
                ? "unattributed"
                : authenticatedId == userId ? "authenticated" : "legacy";

            if (!string.IsNullOrWhiteSpace(userId))
            {
                telemetry.Context.User.Id = CreateAnonymousUserId(userId);
            }

            if (properties != null)
            {
                foreach (var property in properties)
                {
                    telemetry.Properties[property.Key] =
                        NormalizeValue(property.Value, "unknown");
                }
            }

            if (measurements != null)
            {
                foreach (var measurement in measurements)
                {
                    if (!double.IsNaN(measurement.Value)
                        && !double.IsInfinity(measurement.Value))
                    {
                        telemetry.Metrics[measurement.Key] =
                            measurement.Value;
                    }
                }
            }

            _telemetryClient.TrackEvent(telemetry);
        }

        public static string CreateAnonymousUserId(string userId)
        {
            var bytes = Encoding.UTF8.GetBytes(
                AnonymousUserNamespace + userId.Trim());
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash)[..24].ToLowerInvariant();
        }

        private static string NormalizeValue(
            string? value,
            string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
            return normalized.Length <= 128
                ? normalized
                : normalized[..128];
        }
    }
}
