using System.Reflection;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Premium;

internal static class BillingStorageRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var table = new MemoryTable();
        var options = Options.Create(new BillingOptions { Enabled = true });
        var retryTable = new MemoryTable { InitializationFailures = 1 };
        var retryStore = new TableBillingStore(new MemoryTables(retryTable), options);
        check(retryTable.InitializationAttempts == 0, "billing store construction performs no table network initialization");
        try
        {
            await retryStore.BindAsync("retry-owner", default);
            check(false, "first table initialization failure must propagate");
        }
        catch (RequestFailedException ex) { check(ex.Status == 503, "transient initialization failure is surfaced"); }
        var retried = await retryStore.BindAsync("retry-owner", default);
        check((await retryStore.FindCustomerAsync(retried.AppUserId, default))?.RowKey == retried.RowKey
            && retryTable.InitializationAttempts == 2,
            "same store retries table initialization after failure and persists a usable binding");

        var initializing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentTable = new MemoryTable { InitializationWait = initializing.Task };
        var concurrentStore = new TableBillingStore(new MemoryTables(concurrentTable), options);
        using var ownerCancellation = new CancellationTokenSource();
        var owner = concurrentStore.ReadAsync("missing", ownerCancellation.Token);
        using var waiterCancellation = new CancellationTokenSource();
        var waiter = concurrentStore.ReadAsync("missing", waiterCancellation.Token);
        waiterCancellation.Cancel();
        try { await waiter; check(false, "cancelled initializer waiter must throw"); }
        catch (OperationCanceledException) { check(true, "initialization gate observes waiter cancellation"); }
        check(concurrentTable.InitializationAttempts == 1 && !owner.IsCompleted,
            "cancelled waiter neither initializes in parallel nor cancels active initialization");
        ownerCancellation.Cancel();
        try { await owner; check(false, "cancelled table initialization must throw"); }
        catch (OperationCanceledException) { check(true, "table initialization receives caller cancellation"); }
        var following = concurrentStore.ReadAsync("missing", default);
        var alsoFollowing = concurrentStore.ReadAsync("missing", default);
        check(concurrentTable.InitializationAttempts == 2 && !following.IsCompleted && !alsoFollowing.IsCompleted,
            "cancelled initialization is retried and concurrent requests await its completion");
        initializing.SetResult(true);
        await Task.WhenAll(following, alsoFollowing);
        check(concurrentTable.InitializationAttempts == 2, "completed table initialization is reused by waiting requests");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await concurrentStore.ReadAsync("missing", cancelled.Token); check(false, "initialized store must still observe cancellation"); }
        catch (OperationCanceledException) { check(true, "initialized store observes pre-cancelled requests"); }
        TableBillingStore Store() => new(new MemoryTables(table), options);
        var firstStore = Store();
        var first = await firstStore.BindAsync("uid-1", default);
        var again = await Store().BindAsync("uid-1", default);
        var second = await Store().BindAsync("uid-2", default);
        check(first.AppUserId == again.AppUserId && first.AppUserId != second.AppUserId,
            "durable opaque binding survives service restart and is unique per Firebase UID");
        check(first.AppUserId.StartsWith("tfp_") && first.AppUserId.Length == 68,
            "production billing identity uses random 256-bit identifier, not public UID");
        check((await Store().FindCustomerAsync(first.AppUserId, default))?.RowKey == first.RowKey,
            "reverse binding is durably committed alongside UID binding");
        check(await Store().FindCustomerAsync("uid-1", default) == null, "public Firebase UID is not a provider binding");
        var stale = await firstStore.ReadAsync(first.RowKey, default);
        first.ExpiresAt = DateTimeOffset.UtcNow.AddDays(10);
        first.PurchaseOrder = "GPA.1111-2222-3333-44444..0";
        check(await firstStore.SaveAsync(first, "GPA.1111-2222-3333-44444..0", default),
            "subscription and initial purchase ownership are committed atomically");
        check((await Store().ReadAsync(first.RowKey, default))?.PurchaseOrder == first.PurchaseOrder,
            "ownership-checked snapshot marker survives store recreation with the paid state");
        check(!await Store().SaveAsync(stale!, null, default), "stale ETag cannot overwrite newer paid snapshot");
        second.ExpiresAt = DateTimeOffset.UtcNow.AddDays(20);
        second.PurchaseOrder = "GPA.1111-2222-3333-44444..5";
        try
        {
            await Store().SaveAsync(second, "GPA.1111-2222-3333-44444..5", default);
            check(false, "renewal order cannot be re-owned");
        }
        catch (BillingException ex) { check(ex.Code == "billing_ownership_conflict", "renewal order remains owned by original account"); }
        check((await Store().ReadAsync(second.RowKey, default))?.ExpiresAt == null,
            "failed ownership claim cannot partially persist a grant");
        check((await Store().ReadAsync(second.RowKey, default))?.PurchaseOrder == null,
            "failed ownership claim cannot persist a checked-order marker");
        var renewed = await Store().ReadAsync(first.RowKey, default);
        check(await Store().SaveAsync(renewed!, "GPA.1111-2222-3333-44444..6", default), "original owner may renew");
        var inbox = new BillingInbox { RowKey = "event", CustomerIdsJson = "[]" };
        await firstStore.AcceptAsync(inbox, default);
        await firstStore.AcceptAsync(new BillingInbox { RowKey = "event", CustomerIdsJson = "[\"untrusted-change\"]" }, default);
        var storedInbox = await table.GetEntityIfExistsAsync<BillingInbox>("PRODUCTION-Inbox", "event");
        check(storedInbox.Value!.CustomerIdsJson == "[]", "duplicate event cannot overwrite original inbox message");
        var item = storedInbox.Value!;
        var competingItem = (await table.GetEntityIfExistsAsync<BillingInbox>("PRODUCTION-Inbox", "event")).Value!;
        item.RetryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        check(await firstStore.SaveInboxAsync(item, default), "worker reserves event with ETag");
        check(!await firstStore.SaveInboxAsync(competingItem, default), "second worker cannot claim reserved event");
        item.Completed = true;
        check(await firstStore.SaveInboxAsync(item, default), "updated reservation ETag permits completion");
        check((await table.GetEntityIfExistsAsync<BillingInbox>("PRODUCTION-Inbox", "event")).Value!.Completed,
            "successful event completion is durable");
        var sandbox = new TableBillingStore(new MemoryTables(table), Options.Create(new BillingOptions { Environment = "SANDBOX" }));
        var sandboxAccount = await sandbox.BindAsync("uid-1", default);
        check(sandboxAccount.AppUserId.StartsWith("tfs_") && sandboxAccount.AppUserId != first.AppUserId
            && sandboxAccount.ExpiresAt == null, "same Firebase UID is isolated across provider environments");
        check((await Store().FindPlayAccountAsync(TableBillingStore.Hash(first.AppUserId), default))?.RowKey == first.RowKey,
            "Google obfuscated initiating account resolves durable server binding without a client UID");
        var receipt = await Store().ReceiveAsync("play-token", default);
        receipt.AccountKey = first.RowKey;
        receipt.VerifiedAccountId = TableBillingStore.Hash(first.AppUserId);
        receipt.VerifiedProfileId = "verified-profile";
        receipt.OutOfAppResubscription = true;
        receipt.Order = "GPA.9999-2222-3333-44444..0";
        receipt.VerifiedAt = DateTimeOffset.UtcNow;
        receipt.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
        receipt.EligiblePurchaseObservedAt = DateTimeOffset.UtcNow;
        receipt.ProviderStartedAt = DateTimeOffset.UtcNow.AddDays(-1);
        receipt.AcknowledgmentDeadlineEstimateAt = receipt.ProviderStartedAt.Value.AddDays(3);
        receipt.AcknowledgmentState = "pending";
        receipt.AcknowledgmentFirstAttemptAt = DateTimeOffset.UtcNow;
        receipt.AcknowledgmentLastAttemptAt = receipt.AcknowledgmentFirstAttemptAt;
        receipt.AcknowledgmentAttempts = 2;
        receipt.RecoveryCycleStartedAt = DateTimeOffset.UtcNow.AddHours(-2);
        receipt.RecoveryCycleAttempts = 3;
        receipt.RecoveryAttempts = 4;
        await Store().ClaimAsync(receipt, default);
        check(await Store().SaveReceiptAsync(receipt, default), "verified token evidence is durable with an ETag");
        var competing = await Store().ReadReceiptAsync(receipt.Token, default);
        receipt.State = "cancelled";
        check(await Store().SaveReceiptAsync(receipt, default)
            && !await Store().SaveReceiptAsync(competing!, default), "stale verification cannot overwrite newer receipt evidence");
        var restoredReceipt = await Store().VerifiedOrderAsync("GPA.9999-2222-3333-44444..4", first.RowKey, default);
        check(restoredReceipt?.Token == "play-token" && restoredReceipt.ExpiresAt != null,
            "renewal order owner and token survive server restart");
        check(restoredReceipt?.VerifiedAccountId == receipt.VerifiedAccountId
            && restoredReceipt.VerifiedProfileId == receipt.VerifiedProfileId && restoredReceipt.OutOfAppResubscription,
            "actual Table store retains verified resubscription identifiers across service recreation");
        var requeued = await Store().ReceiveAsync("play-token", default);
        check(requeued.EligiblePurchaseObservedAt == receipt.EligiblePurchaseObservedAt
            && requeued.ProviderStartedAt == receipt.ProviderStartedAt
            && requeued.AcknowledgmentDeadlineEstimateAt == receipt.AcknowledgmentDeadlineEstimateAt
            && requeued.AcknowledgmentState == "pending" && requeued.AcknowledgmentAttempts == 2
            && requeued.AcknowledgmentFirstAttemptAt == receipt.AcknowledgmentFirstAttemptAt
            && requeued.RecoveryCycleStartedAt == receipt.RecoveryCycleStartedAt
            && requeued.RecoveryCycleAttempts == 3 && requeued.RecoveryAttempts == 4,
            "Table replacement writes preserve acknowledgment and retry clocks across requeue and store recreation");
        check(requeued.ExpiresAt == receipt.ExpiresAt && requeued.VerifiedAt == receipt.VerifiedAt
            && requeued.VerifiedAccountId == receipt.VerifiedAccountId && !requeued.Completed,
            "actual Table store requeues linked original without discarding paid expiry or verified ownership");
        var conflicting = await Store().ReceiveAsync("other-token", default);
        conflicting.Order = "GPA.9999-2222-3333-44444..9";
        conflicting.AccountKey = second.RowKey;
        try { await Store().ClaimAsync(conflicting, default); check(false, "root Play order claimed twice"); }
        catch (BillingException ex) { check(ex.Code == "billing_ownership_conflict", "Play verified ownership claim cannot transfer at renewal"); }
        await Store().SupersedeAsync("play-token", "replacement-token", first.RowKey, default);
        var replaced = await Store().ReceiveAsync("play-token", default);
        check(replaced.SupersededByToken == "replacement-token" && replaced.ExpiresAt == null,
            "late notification cannot remove immutable subscription replacement revocation");
        check(await sandbox.ReadReceiptAsync("play-token", default) == null, "Google purchase tokens are isolated by environment");
    }

    internal sealed class MemoryTables : TableServiceClient
    {
        private readonly TableClient _table;
        public MemoryTables(TableClient table) => _table = table;
        public override TableClient GetTableClient(string tableName) => _table;
    }
    internal sealed class MemoryTable : TableClient
    {
        private readonly Dictionary<(string, string), ITableEntity> _rows = new();
        private int _version;
        public int InitializationAttempts { get; private set; }
        public int InitializationFailures { get; set; }
        public Task? InitializationWait { get; set; }
        private static ITableEntity Copy(ITableEntity value) => value is TableEntity table
            ? new TableEntity(table.ToDictionary(p => p.Key, p => p.Value)) { PartitionKey = table.PartitionKey, RowKey = table.RowKey, ETag = table.ETag }
            : (ITableEntity)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
        public override async Task<Response<TableItem>> CreateIfNotExistsAsync(CancellationToken cancellationToken = default)
        {
            InitializationAttempts++;
            cancellationToken.ThrowIfCancellationRequested();
            if (InitializationFailures-- > 0) throw new RequestFailedException(503, "retryable initialization failure");
            if (InitializationWait != null) await InitializationWait.WaitAsync(cancellationToken);
            return Response.FromValue<TableItem>(null!, new FakeResponse());
        }
        public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            lock (_rows) return Task.FromResult<NullableResponse<T>>(new OptionalResponse<T>(
                _rows.TryGetValue((partitionKey, rowKey), out var row) ? (T)Copy(row) : null));
        }
        public override Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(IEnumerable<TableTransactionAction> transactionActions,
            CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                var actions = transactionActions.ToArray();
                foreach (var action in actions)
                {
                    var exists = _rows.TryGetValue((action.Entity.PartitionKey, action.Entity.RowKey), out var row);
                    if (action.ActionType == TableTransactionActionType.Add && exists) throw new RequestFailedException(409, "exists");
                    if (action.ActionType == TableTransactionActionType.UpdateReplace && (!exists || row!.ETag != action.ETag))
                        throw new RequestFailedException(412, "conflict");
                }
                var responses = actions.Select(action => Store(action.Entity)).ToArray();
                return Task.FromResult(Response.FromValue<IReadOnlyList<Response>>(responses, new FakeResponse()));
            }
        }
        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                if (_rows.ContainsKey((entity.PartitionKey, entity.RowKey))) throw new RequestFailedException(409, "exists");
                return Task.FromResult(Store(entity));
            }
        }
        public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                if (!_rows.TryGetValue((entity.PartitionKey, entity.RowKey), out var current) || current.ETag != ifMatch)
                    throw new RequestFailedException(412, "conflict");
                return Task.FromResult(Store(entity));
            }
        }
        private Response Store(ITableEntity entity)
        {
            var copy = Copy(entity);
            copy.ETag = new ETag("\"" + ++_version + "\"");
            _rows[(entity.PartitionKey, entity.RowKey)] = copy;
            return new FakeResponse(copy.ETag.ToString());
        }
    }
    private sealed class OptionalResponse<T> : NullableResponse<T> where T : class
    {
        private readonly T? _value;
        public OptionalResponse(T? value) => _value = value;
        public override bool HasValue => _value != null;
        public override T Value => _value!;
        public override Response GetRawResponse() => new FakeResponse();
    }
    private sealed class FakeResponse : Response
    {
        private readonly string _etag;
        public FakeResponse(string etag = "\"1\"") => _etag = etag;
        public override int Status => 204;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "";
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => name.Equals("ETag", StringComparison.OrdinalIgnoreCase);
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => new[] { new HttpHeader("ETag", _etag) };
        protected override bool TryGetHeader(string name, out string value) { value = _etag; return ContainsHeader(name); }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        { values = new[] { _etag }; return ContainsHeader(name); }
    }
}
