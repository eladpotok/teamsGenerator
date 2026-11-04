using System.Text.RegularExpressions;

namespace TeamsGeneratorWebAPI.Telemetry;

public sealed record WebsiteVisitRequest(string? VisitorId, string? SessionId, string? Source,
    string? Medium, string? Campaign, string? ReferrerHost, string? LandingPage);

public static class WebsiteAttribution
{
    private static readonly Regex Tag = new(@"\A[a-z0-9][a-z0-9._~-]{0,79}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Host = new(
        @"\A(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,63}\z", RegexOptions.CultureInvariant);

    public static Dictionary<string, string?>? Properties(WebsiteVisitRequest? value)
    {
        if (value == null || !Guid.TryParseExact(value.VisitorId, "D", out var visitor)
            || visitor == Guid.Empty || !Guid.TryParseExact(value.SessionId, "D", out var session)
            || session == Guid.Empty
            || !ValidTag(value.Source) || !ValidTag(value.Medium) || !ValidTag(value.Campaign)
            || value.ReferrerHost == null || value.ReferrerHost.Length > 128
            || (value.ReferrerHost != "none" && !Host.IsMatch(value.ReferrerHost))
            || value.LandingPage is not ("app" or "invitation" or "group_join" or "poll" or "other"))
            return null;

        return new()
        {
            ["visitor_key"] = UsageMetrics.ResourceKey("website-visitor", visitor.ToString("D")),
            ["session_key"] = UsageMetrics.ResourceKey("website-session", visitor.ToString("D"), session.ToString("D")),
            ["utm_source"] = value.Source, ["utm_medium"] = value.Medium,
            ["utm_campaign"] = value.Campaign, ["referrer_host"] = value.ReferrerHost,
            ["landing_page"] = value.LandingPage, ["client_channel"] = "web",
            ["attribution_source"] = "client_reported", ["attribution_version"] = "1"
        };
    }

    public static Dictionary<string, string?>? FromHeaders(IHeaderDictionary headers) =>
        Properties(new WebsiteVisitRequest(headers["X-Teamify-Visitor"], headers["X-Teamify-Session"],
            headers["X-Teamify-Source"], headers["X-Teamify-Medium"], headers["X-Teamify-Campaign"],
            headers["X-Teamify-Referrer"], headers["X-Teamify-Landing"]));

    private static bool ValidTag(string? value) =>
        value is { Length: > 0 and <= 80 } && Tag.IsMatch(value);
}
