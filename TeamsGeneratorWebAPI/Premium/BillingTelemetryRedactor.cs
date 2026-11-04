using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace TeamsGeneratorWebAPI.Premium;

public sealed class BillingTelemetryRedactor : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        if (telemetry is DependencyTelemetry dependency
            && (dependency.Target?.Contains("api.revenuecat.com", StringComparison.OrdinalIgnoreCase) == true
                || dependency.Data?.Contains("api.revenuecat.com", StringComparison.OrdinalIgnoreCase) == true))
        {
            dependency.Data = "https://api.revenuecat.com/v1/subscribers/[redacted]";
            dependency.Name = "GET /v1/subscribers/[redacted]";
        }
        if (telemetry is DependencyTelemetry play
            && (play.Target?.Contains("androidpublisher.googleapis.com", StringComparison.OrdinalIgnoreCase) == true
                || play.Data?.Contains("androidpublisher.googleapis.com", StringComparison.OrdinalIgnoreCase) == true))
        {
            play.Data = "https://androidpublisher.googleapis.com/[redacted]";
            play.Name = "Google Play purchase verification";
        }
    }
}
