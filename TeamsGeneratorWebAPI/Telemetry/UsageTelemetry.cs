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

        public UsageTelemetry(
            TelemetryClient telemetryClient,
            IHostEnvironment environment)
        {
            _telemetryClient = telemetryClient;
            _environmentName = environment.EnvironmentName;
        }

        public void Track(
            string eventName,
            string? clientVersion = null,
            string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null)
        {
            var telemetry = new EventTelemetry(eventName);
            telemetry.Properties["client_version"] =
                NormalizeValue(clientVersion, "unknown");
            telemetry.Properties["environment"] =
                NormalizeValue(_environmentName, "unknown");

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

        private static string CreateAnonymousUserId(string userId)
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
