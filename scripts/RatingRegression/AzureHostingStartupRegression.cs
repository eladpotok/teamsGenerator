using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.AzureAppServices;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI;

internal static class AzureHostingStartupRegression
{
    internal static async Task Run(Action<bool, string> check)
    {
        // This host deliberately never invokes the API entry point, configuration or cloud DI.
        var root = Environment.GetEnvironmentVariable("TEAMIFY_REHEARSAL_ROOT");
        check(root != null && Directory.Exists(root), "isolated synthetic content root exists");
        check(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") == "teamify-local-rehearsal",
            "Azure diagnostics context is synthetic");
        check(Environment.GetEnvironmentVariable("HOME") == root, "diagnostics HOME is isolated");
        using var capture = new StartupLogCapture();
        using var host = new WebHostBuilder()
            .UseContentRoot(root!)
            .UseSetting(WebHostDefaults.HostingStartupAssembliesKey,
                "Microsoft.AspNetCore.AzureAppServices.HostingStartup")
            .UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0))
            .ConfigureLogging(logging => logging.AddProvider(capture))
            .ConfigureServices(services => services.AddRouting())
            .Configure(app =>
            {
                app.UseDeploymentIdentity();
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGet("/health", ApplicationHealth.Read));
            })
            .Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await host.StartAsync(deadline.Token);
        try
        {
            var address = host.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            check(new Uri(address).IsLoopback, "rehearsal binds only loopback");
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            using var response = await client.GetAsync(address + "/health", deadline.Token);
            var body = await response.Content.ReadAsStringAsync(deadline.Token);
            check(body == """{"status":"healthy"}""", "synthetic health contract is unchanged");
            check(response.Headers.GetValues(ApplicationHealth.RevisionHeader).Single() == ApplicationHealth.Revision,
                "HTTP response identifies the actual loaded API module");
            check(response.Headers.GetValues(ApplicationHealth.RuntimeHeader).Single() == Environment.Version.ToString(),
                "HTTP response identifies the actual loaded CLR");
            using var missing = new HttpRequestMessage(HttpMethod.Get, address + "/synthetic-missing");
            missing.Headers.Add(ApplicationHealth.ReadinessHeader, "1");
            using var missingResponse = await client.SendAsync(missing, deadline.Token);
            check(missingResponse.StatusCode == System.Net.HttpStatusCode.NotFound
                && missingResponse.Headers.GetValues(ApplicationHealth.RevisionHeader).Single() == ApplicationHealth.Revision,
                "opt-in route probes identify their own serving module even on 404");
            using var ordinary = await client.GetAsync(address + "/synthetic-missing", deadline.Token);
            check(!ordinary.Headers.Contains(ApplicationHealth.RevisionHeader),
                "ordinary non-health routes do not add deployment headers");
            check(capture.Errors.Count == 0, "injected Azure startup has no Event11 or startup errors");
            var startup = AppDomain.CurrentDomain.GetAssemblies()
                .Single(a => a.GetName().Name == "Microsoft.AspNetCore.AzureAppServices.HostingStartup");
            check(startup.GetName().Version?.Major == 10, "injected startup resolves the net10 application dependency");
            var providers = host.Services.GetServices<ILoggerProvider>().ToArray();
            check(providers.Any(p => p is FileLoggerProvider), "injection registers Azure file logging");
            check(providers.Any(p => p is BlobLoggerProvider), "injection registers Azure blob logging");
            check(!host.Services.GetRequiredService<IOptions<AzureBlobLoggerOptions>>().Value.IsEnabled,
                "synthetic blob logging is disabled");
            check(!host.Services.GetRequiredService<IOptions<AzureFileLoggerOptions>>().Value.IsEnabled,
                "synthetic file logging is disabled");
            check(Environment.Version.Major == 10, "synthetic host loads .NET10");
            Console.WriteLine($"Isolated CLR {Environment.Version}; API revision {ApplicationHealth.Revision}; Azure startup assembly {startup.GetName().Version}.");
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private sealed class StartupLogCapture : ILoggerProvider, ILogger
    {
        internal readonly System.Collections.Concurrent.ConcurrentQueue<int> Errors = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
        public void Log<TState>(LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Error) Errors.Enqueue(eventId.Id);
        }
        public void Dispose() { }
    }
}
