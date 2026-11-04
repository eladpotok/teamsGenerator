using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Premium;

internal static class BillingSnapshotRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var options = Options.Create(new BillingOptions { Enabled = true });
        var store = new BillingRegression.MemoryStore();
        var account = await store.BindAsync("snapshot-owner", default);
        var now = DateTimeOffset.UtcNow;
        var receipt = new PlayReceipt { AccountKey = account.RowKey, Token = "synthetic",
            VerifiedAt = now, ExpiresAt = now.AddDays(10), State = "active", Order = "GPA.synthetic..0" };
        var queries = 0;
        var receipts = CallbackProxy.Create<IPlayPurchaseStore>((method, args) =>
        {
            if (method.Name != "AccountReceiptsAsync") throw new InvalidOperationException(method.Name);
            queries++;
            return Task.FromResult<IReadOnlyList<PlayReceipt>>(new[]
                { JsonSerializer.Deserialize<PlayReceipt>(JsonSerializer.Serialize(receipt))! });
        });
        var manualPremium = false;
        var manualReads = 0;
        Action? duringManualRead = null;
        var manual = CallbackProxy.Create<IPremiumAccountStore>((method, args) =>
        {
            manualReads++;
            duringManualRead?.Invoke();
            return Task.FromResult(manualPremium);
        });
        var billing = new BillingService(store, options, receipts);
        var entitlements = new AccountEntitlementService(new ConfigurationBuilder().Build(), manual, billing: billing);
        var controller = new BillingController(billing, store, entitlements, options)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
                { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "snapshot-owner") }, "test")) } }
        };
        void Reset() { store.ReadCalls = store.SaveCalls = queries = manualReads = 0; }
        JsonElement Body(IActionResult result) => JsonSerializer.SerializeToElement(((OkObjectResult)result).Value);
        void Consistent(JsonElement body, bool paid, bool premium) =>
            check(body.GetProperty("paidActive").GetBoolean() == paid
                && body.GetProperty("entitlements").GetProperty("IsPremium").GetBoolean() == premium,
                "status paid state and capabilities use the same billing snapshot");

        await billing.ReadAsync("snapshot-owner", default);
        check(store.SaveCalls == 1 && store.Account!.PurchaseOrder == receipt.Order,
            "first observation persists paid state and its ownership-checked order");
        foreach (var sync in new[] { false, true })
        {
            Reset();
            var retryAt = store.Account!.RetryAt;
            Consistent(Body(await (sync ? controller.Sync(default) : controller.Status(default))), true, true);
            check(store.ReadCalls == 1 && queries == 1 && store.SaveCalls == 0 && manualReads == 1,
                (sync ? "POST sync" : "GET status") + " uses one account read, one receipt query, zero unchanged writes");
            check(store.Account.RetryAt == retryAt, "foreground status does not postpone reconciliation");
        }

        store.Account!.RetryAt = now.AddMinutes(-1);
        Reset();
        await billing.ReadAsync("snapshot-owner", default);
        check(store.SaveCalls == 0 && store.Account.RetryAt < now, "foreground read leaves a due worker due");
        await billing.SyncAsync(store.Account, default);
        check(store.SaveCalls == 1 && store.Account.RetryAt > now, "due worker advances its durable schedule once");
        await billing.SyncAsync(store.Account, default);
        check(store.SaveCalls == 1, "not-due unchanged reconciliation does not write");

        store.Account.PurchaseOrder = null;
        Reset();
        await billing.ReadAsync("snapshot-owner", default);
        check(store.SaveCalls == 1 && store.Account.PurchaseOrder == receipt.Order,
            "legacy snapshots recheck and backfill the purchase ownership marker");
        receipt.Order = "GPA.different..0";
        Reset();
        await billing.ReadAsync("snapshot-owner", default);
        check(store.SaveCalls == 1 && store.Account.PurchaseOrder == receipt.Order,
            "changed order cannot bypass ownership claim even when entitlement fields are identical");

        receipt.Order = "GPA.conflict..0";
        store.RejectOwnership = true;
        Consistent(Body(await controller.Status(default)), false, false);
        check(store.Account!.Status == "ownership_conflict" && store.Account.PurchaseOrder == null,
            "ownership failure cannot persist a paid grant or a checked-order marker");
        store.RejectOwnership = false;
        await controller.Sync(default);

        receipt.State = "cancelled";
        store.ConflictWrites = 1;
        store.BeforeSave = () =>
        {
            store.BeforeSave = null;
            receipt.ExpiresAt = null;
            receipt.State = "revoked";
        };
        Reset();
        Consistent(Body(await controller.Sync(default)), false, false);
        check(store.ReadCalls == 2 && queries == 2 && store.SaveCalls == 2 && store.Account!.ExpiresAt == null,
            "ETag retry refreshes both account and receipt evidence instead of overwriting a concurrent revocation");

        receipt.ExpiresAt = now.AddDays(-1);
        receipt.State = "expired";
        await controller.Sync(default);
        Reset();
        Consistent(Body(await controller.Status(default)), false, false);
        check(store.SaveCalls == 0, "unchanged expired subscription remains inactive without writes");
        receipt.ExpiresAt = now.AddDays(10);
        receipt.VerifiedAt = now.AddHours(-options.Value.MaximumStaleHours - 1);
        Consistent(Body(await controller.Sync(default)), false, false);
        receipt.VerifiedAt = now;
        receipt.SupersededByToken = "replacement";
        Consistent(Body(await controller.Sync(default)), false, false);
        manualPremium = true;
        Consistent(Body(await controller.Status(default)), false, true);
        manualPremium = false;
        receipt.SupersededByToken = null;
        receipt.State = "active";
        await controller.Sync(default);
        duringManualRead = () => { receipt.ExpiresAt = null; receipt.State = "revoked"; };
        Reset();
        Consistent(Body(await controller.Status(default)), true, true);
        check(queries == 1, "manual entitlement lookup cannot trigger a second inconsistent paid snapshot");
        duringManualRead = null;
        Consistent(Body(await controller.Status(default)), false, false);

        store.Account = null;
        Reset();
        Consistent(Body(await controller.Status(default)), false, false);
        check(store.ReadCalls == 1 && queries == 0 && store.SaveCalls == 0,
            "missing binding is read once without receipt queries or writes");
        check((await controller.Sync(default) as ObjectResult)?.StatusCode == 409,
            "sync still requires an existing binding");
    }
}
