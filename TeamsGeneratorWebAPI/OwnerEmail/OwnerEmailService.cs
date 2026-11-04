using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Premium;

namespace TeamsGeneratorWebAPI.OwnerEmail;

public sealed class OwnerEmailService(IOwnerEmailStore store, IOptions<OwnerEmailOptions> options)
{
    public bool FeedbackEnabled => options.Value.Enabled && options.Value.FeedbackEnabled;

    public Task AcceptFeedbackAsync(string uid, string? verifiedEmail, string? category, string? message,
        string? idempotencyKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!FeedbackEnabled) throw new OwnerEmailException(503, "feedback_disabled");
        if (category is not ("feedback" or "question") || string.IsNullOrWhiteSpace(message)
            || message.Length > 4000 || message.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new OwnerEmailException(400, "feedback_invalid");
        if (idempotencyKey != null && !Guid.TryParseExact(idempotencyKey, "D", out _))
            throw new OwnerEmailException(400, "feedback_invalid_idempotency_key");
        var content = category + "\n" + message.Trim();
        var userKey = TableBillingStore.Hash(uid);
        // Content-derived fallback deduplicates older clients, without permitting client-selected storage keys.
        var key = TableBillingStore.Hash(uid + "\n" + (idempotencyKey ?? content));
        var reply = OwnerEmailOptions.SafeAddress(verifiedEmail) ? "\nVerified reply email: " + verifiedEmail : "";
        return store.AcceptAsync(new OwnerEmailItem
        {
            RowKey = "feedback-" + key, ContentHash = TableBillingStore.Hash(content),
            Subject = category == "question" ? "Teamify - app question" : "Teamify - app feedback",
            Body = "New Teamify " + category + reply + "\n\n" + message.Trim()
        }, userKey, ct);
    }
}
