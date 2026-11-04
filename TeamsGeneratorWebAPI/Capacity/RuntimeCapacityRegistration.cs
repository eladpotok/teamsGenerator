using Azure.Core;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.Capacity;

public static class RuntimeCapacityRegistration
{
    public static IServiceCollection AddRuntimeCapacity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CapacityOptions>().Bind(configuration.GetSection("Capacity"))
            .Validate(options => typeof(CapacityOptions).GetProperties()
                .Where(property => property.PropertyType == typeof(int))
                .All(property => (int)property.GetValue(options)! > 0), "Capacity limits must be positive.")
            .Validate(options => options.FreeGenerationPlayers <= options.PremiumGenerationPlayers
                && options.MaximumRenderingEntries <= 1000
                && options.ConcurrentGenerationsPerCaller <= options.GlobalExpensiveConcurrency
                && options.PremiumGenerationPlayers <= 100
                && options.MaximumGenerationTeams <= 20
                && options.OperationTimeoutSeconds <= 300
                && options.GlobalExpensiveConcurrency <= 32
                && options.GenerationRequestsPerMinute <= 1000
                && options.GlobalGenerationRequestsPerMinute <= 1000
                && options.WritesPerMinute * (long)options.GlobalExpensiveConcurrency <= 1000
                && options.MaximumRequestBytes <= 16 * 1024 * 1024,
                "Capacity values exceed supported runtime safety bounds.")
            .ValidateOnStart();
        services.AddSingleton<IRuntimeClock, RuntimeClock>();
        services.AddSingleton<IRuntimeCapacityState>(provider =>
        {
            var connection = configuration.GetValue<string>("BlobConnectionString");
            var tables = new TableClientOptions();
            var blobs = new BlobClientOptions();
            Configure(tables.Retry);
            Configure(blobs.Retry);
            return new RuntimeCapacityState(new TableClient(connection, "TeamifyRuntimeCapacity", tables),
                new BlobContainerClient(connection, "teamify-runtime-capacity", blobs),
                provider.GetRequiredService<IRuntimeClock>(),
                provider.GetRequiredService<ILogger<RuntimeCapacityState>>());
        });
        return services;
    }

    private static void Configure(RetryOptions retry)
    {
        retry.MaxRetries = 1;
        retry.NetworkTimeout = TimeSpan.FromSeconds(5);
        retry.Delay = TimeSpan.FromMilliseconds(200);
    }
}
