using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Premium;

namespace TeamsGeneratorWebAPI.OwnerEmail;

public sealed class OwnerPaidAlertService(IOwnerEmailStore store, IOptions<OwnerEmailOptions> options)
{
    public bool AcceptsEvent(DateTimeOffset? purchasedAt) => options.Value.Enabled && options.Value.PaidAlertsEnabled
        && purchasedAt.HasValue && purchasedAt >= options.Value.PaidAlertsNotBefore
        && purchasedAt <= DateTimeOffset.UtcNow.AddMinutes(5);

    public void Prepare(PlayReceipt receipt)
    {
        if (receipt.OwnerAlertKey != null || !AcceptsEvent(receipt.NewPurchaseEventAt) || !receipt.PaidInitialOrder
            || receipt.Sandbox || receipt.SupersededByToken != null || receipt.OutOfAppResubscription
            || receipt.AccountKey == null || receipt.Order == null
            || !receipt.ProviderStartedAt.HasValue || receipt.ProviderStartedAt < options.Value.PaidAlertsNotBefore
            || receipt.VerifiedAt == null || !receipt.ExpiresAt.HasValue || receipt.ExpiresAt <= DateTimeOffset.UtcNow)
            return;
        // Google and RevenueCat converge on the same verified root order, never their event IDs.
        var rootOrder = receipt.Order.Split(new[] { ".." }, StringSplitOptions.None)[0];
        receipt.OwnerAlertKey = TableBillingStore.Hash("google-play\n" + rootOrder);
        receipt.OwnerAlertPending = true;
    }

    public Task ObserveAsync(PlayReceipt receipt, CancellationToken ct)
    {
        Prepare(receipt);
        if (!AcceptsEvent(receipt.NewPurchaseEventAt) || !receipt.OwnerAlertPending || receipt.OwnerAlertKey is not { } key)
            return Task.CompletedTask;
        return store.AcceptAsync(new OwnerEmailItem
        {
            RowKey = "paid-" + key, ContentHash = key,
            Subject = "Teamify - new paid subscription",
            Body = "A new Google Play paid Teamify Premium subscription was verified.\n"
                + "This is a new purchase, not a trial or renewal.\nReference: " + key[..12]
        }, null, ct);
    }
}
