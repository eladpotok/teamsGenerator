using Azure;
using Azure.Data.Tables;
using System.Linq.Expressions;
using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.Clients
{
    public class AzureTableStorageService
    {
        private const int MaximumLifecycleAttempts = 5;
        private readonly TableClient _tableClient;

        public static string RowKeyForCloseStatus = "ReservedToStatus";
        public static string RowKeyForStartStatus = "ReservedToStatusStart";
        public const string PlayerSwapRowKeyPrefix = "PlayerSwap_";

        public static HashSet<string> MatchesForStatusKeys = new HashSet<string>() 
        {
            RowKeyForCloseStatus,
            RowKeyForStartStatus
        };

        public AzureTableStorageService(TableServiceClient tableServiceClient)
        {
            // This will create the table if it doesn't exist
            _tableClient = tableServiceClient.GetTableClient("Matches");
            _tableClient.CreateIfNotExists();
        }

        internal AzureTableStorageService(TableClient tableClient) => _tableClient = tableClient;

        internal async Task<MatchMutationResult> AddMatchAsync(
            MatchEntity match)
        {
            ArgumentNullException.ThrowIfNull(match);
            ValidateMatchIdentity(match);
            match.ETag = default;
            match.Timestamp = null;

            for (var attempt = 0; attempt < MaximumLifecycleAttempts; attempt++)
            {
                var status = await GetOrCreateMatchdayStatus(
                    match.PartitionKey);
                if (status.IsClosed)
                {
                    return MatchMutationResult.Closed;
                }

                var lastSavedAt = status.LastMatchCreatedAt
                    ?? (await GetAllMatchesAsync(match.PartitionKey))
                        .LastOrDefault()?.CreatedAt;
                match.CreatedAt = DateTime.UtcNow;
                if (lastSavedAt.HasValue && match.CreatedAt < lastSavedAt.Value.AddMilliseconds(1))
                {
                    match.CreatedAt = lastSavedAt.Value.AddMilliseconds(1);
                }

                try
                {
                    await _tableClient.SubmitTransactionAsync(new[]
                    {
                        CreateStatusGuardAction(status, match.CreatedAt),
                        new TableTransactionAction(
                            TableTransactionActionType.Add,
                            match)
                    });
                    return MatchMutationResult.Succeeded;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 412
                        || exception.Status == 404)
                {
                    continue;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 409)
                {
                    return await IsClosed(match.PartitionKey)
                        ? MatchMutationResult.Closed
                        : MatchMutationResult.Conflict;
                }
            }

            return MatchMutationResult.Conflict;
        }

        internal async Task<MatchMutationResult> AddPlayerSwapAsync(
            PlayerSwapEntity playerSwap)
        {
            ArgumentNullException.ThrowIfNull(playerSwap);
            if (string.IsNullOrWhiteSpace(playerSwap.PartitionKey)
                || string.IsNullOrWhiteSpace(playerSwap.RowKey)
                || string.IsNullOrWhiteSpace(playerSwap.SerializedSwap))
            {
                throw new ArgumentException(
                    "Matchday, swap identity, and swap details are required.");
            }
            if (!playerSwap.RowKey.StartsWith(
                PlayerSwapRowKeyPrefix,
                StringComparison.Ordinal))
            {
                playerSwap.RowKey =
                    PlayerSwapRowKeyPrefix + playerSwap.RowKey;
            }

            playerSwap.CreatedAt = DateTime.UtcNow;
            playerSwap.ETag = default;
            playerSwap.Timestamp = null;

            for (var attempt = 0; attempt < MaximumLifecycleAttempts; attempt++)
            {
                var status = await GetOrCreateMatchdayStatus(
                    playerSwap.PartitionKey);
                if (status.IsClosed)
                {
                    return MatchMutationResult.Closed;
                }

                try
                {
                    await _tableClient.SubmitTransactionAsync(new[]
                    {
                        CreateStatusGuardAction(status),
                        new TableTransactionAction(
                            TableTransactionActionType.Add,
                            playerSwap)
                    });
                    return MatchMutationResult.Succeeded;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 412
                        || exception.Status == 404)
                {
                    continue;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 409)
                {
                    return await IsClosed(playerSwap.PartitionKey)
                        ? MatchMutationResult.Closed
                        : MatchMutationResult.Conflict;
                }
            }

            return MatchMutationResult.Conflict;
        }

        public async Task AddUpdate(UpdateEntity update)
        {
            await _tableClient.AddEntityAsync(update);
        }

        public async Task AddFeedback(FeedbackEntity feedback)
        {
            await _tableClient.AddEntityAsync(feedback);
        }

        public async Task AddEntity<T>(T entity, CancellationToken cancellationToken = default)
               where T : class, ITableEntity, new()
        {
            await _tableClient.AddEntityAsync(entity);
        }

        public async Task<List<MatchEntity>> GetAllMatchesAsync(string partitionKey, CancellationToken ct = default)
        {
            var matches = new List<MatchEntity>();
            await foreach (var entity in _tableClient
                .QueryAsync<MatchEntity>(
                    e => e.PartitionKey == partitionKey, cancellationToken: ct))
            {
                matches.Add(entity);
            }

            return matches
                .Where(row =>
                    !MatchesForStatusKeys.Contains(row.RowKey)
                    && !row.RowKey.StartsWith(
                        PlayerSwapRowKeyPrefix,
                        StringComparison.Ordinal))
                .OrderBy(match => match.CreatedAt)
                .ToList();
        }

        internal async Task<List<PlayerSwapEntity>> GetAllPlayerSwapsAsync(
            string partitionKey, CancellationToken ct = default)
        {
            var playerSwaps = new List<PlayerSwapEntity>();
            await foreach (var entity in _tableClient
                .QueryAsync<PlayerSwapEntity>(
                    item => item.PartitionKey == partitionKey, cancellationToken: ct))
            {
                if (entity.RowKey.StartsWith(
                    PlayerSwapRowKeyPrefix,
                    StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(entity.SerializedSwap))
                {
                    playerSwaps.Add(entity);
                }
            }

            return playerSwaps
                .OrderBy(playerSwap => playerSwap.CreatedAt)
                .ThenBy(playerSwap => playerSwap.RowKey)
                .ToList();
        }

        internal async Task<(IReadOnlyList<MatchEntity> Matches, IReadOnlyList<PlayerSwapEntity> Swaps)>
            GetMatchdayEventsAsync(string partitionKey, CancellationToken ct)
        {
            var matches = new List<MatchEntity>();
            var swaps = new List<PlayerSwapEntity>();
            await foreach (var row in _tableClient.QueryAsync<MatchdayEventEntity>(
                item => item.PartitionKey == partitionKey,
                select: new[] { "PartitionKey", "RowKey", "SerializedMatch", "SerializedSwap", "CreatedAt" },
                cancellationToken: ct))
            {
                if (MatchesForStatusKeys.Contains(row.RowKey)) continue;
                if (row.RowKey.StartsWith(PlayerSwapRowKeyPrefix, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(row.SerializedSwap))
                        swaps.Add(new PlayerSwapEntity { PartitionKey = row.PartitionKey, RowKey = row.RowKey,
                            SerializedSwap = row.SerializedSwap, CreatedAt = row.CreatedAt });
                }
                else
                    matches.Add(new MatchEntity { PartitionKey = row.PartitionKey, RowKey = row.RowKey,
                        SerializedMatch = row.SerializedMatch!, CreatedAt = row.CreatedAt });
            }
            return (matches.OrderBy(match => match.CreatedAt).ToArray(),
                swaps.OrderBy(swap => swap.CreatedAt).ThenBy(swap => swap.RowKey).ToArray());
        }

        public async Task<List<T>> GetAllEntities<T>(string partitionKey, CancellationToken cancellationToken = default)
                where T : class, ITableEntity, new()
        {
            var matches = new List<T>();
            await foreach (var entity in _tableClient.QueryAsync<T>(e => e.PartitionKey == partitionKey, cancellationToken: cancellationToken))
            {
                matches.Add(entity);
            }
            return matches.OrderByDescending(t => t.Timestamp).ToList();
        }

        public async Task<T> GetSingleEntitiy<T>(string partitionKey, CancellationToken cancellationToken = default)
                where T : class, ITableEntity, new()
        {
            var entities = await GetAllEntities<T>(partitionKey, cancellationToken);
            if(entities.Count == 0)
            {
                throw new KeyNotFoundException();
            }

            return entities.Single();
        }

        internal async Task<List<MatchEntity>> FinalizeMatchday(
            string partitionKey,
            string? ownerId)
        {
            if (string.IsNullOrWhiteSpace(partitionKey))
            {
                throw new ArgumentException(
                    "A matchday identifier is required.",
                    nameof(partitionKey));
            }

            for (var attempt = 0; attempt < MaximumLifecycleAttempts; attempt++)
            {
                var status = await GetOrCreateMatchdayStatus(partitionKey);
                var matches = await GetAllMatchesAsync(partitionKey);
                var completedAt = status.IsClosed
                    ? status.Timestamp ?? DateTimeOffset.UtcNow
                    : DateTimeOffset.UtcNow;

                if (status.IsClosed)
                {
                    if (!string.IsNullOrWhiteSpace(ownerId)
                        && matches.Count > 0)
                    {
                        await StoreChemistryMatchday(
                            ownerId,
                            partitionKey,
                            matches,
                            completedAt,
                            true);
                    }

                    return matches;
                }

                var closedStatus = CopyStatus(status, true);
                try
                {
                    await _tableClient.UpdateEntityAsync(
                        closedStatus,
                        status.ETag,
                        TableUpdateMode.Replace);
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 412
                        || exception.Status == 404)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(ownerId)
                    && matches.Count > 0)
                {
                    await StoreChemistryMatchday(
                        ownerId,
                        partitionKey,
                        matches,
                        completedAt,
                        false);
                }

                return matches;
            }

            throw new MatchdayConcurrencyException(
                "The matchday changed while it was being finalized.");
        }

        private async Task StoreChemistryMatchday(
            string ownerId,
            string matchdayId,
            IEnumerable<MatchEntity> matches,
            DateTimeOffset completedAt,
            bool preserveExistingCompletedAt)
        {
            if (string.IsNullOrWhiteSpace(ownerId)
                || string.IsNullOrWhiteSpace(matchdayId))
            {
                throw new ArgumentException(
                    "Owner and matchday identifiers are required.");
            }

            for (var attempt = 0; attempt < MaximumLifecycleAttempts; attempt++)
            {
                var entity = ChemistryHistory.CreateMatchdayEntity(
                    ownerId,
                    matchdayId,
                    matches,
                    completedAt);
                var existingResponse = await _tableClient
                    .GetEntityIfExistsAsync<ChemistryMatchdayEntity>(
                        entity.PartitionKey,
                        entity.RowKey);

                try
                {
                    if (!existingResponse.HasValue)
                    {
                        await _tableClient.AddEntityAsync(entity);
                        return;
                    }

                    var existing = existingResponse.Value;
                    if (preserveExistingCompletedAt
                        && existing.CompletedAt != default)
                    {
                        entity.CompletedAt = existing.CompletedAt;
                    }

                    await _tableClient.UpdateEntityAsync(
                        entity,
                        existing.ETag,
                        TableUpdateMode.Replace);
                    return;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 409
                        || exception.Status == 412
                        || exception.Status == 404)
                {
                    continue;
                }
            }

            throw new MatchdayConcurrencyException(
                "The chemistry history changed while it was being stored.");
        }

        internal async Task ResetChemistryHistory(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new ArgumentException("An owner identifier is required.");
            }

            // A separate partition keeps this marker out of the matchday evidence.
            // Its storage timestamp orders concurrent resets without a client clock.
            await _tableClient.UpsertEntityAsync(
                new TableEntity(
                    ChemistryHistory.GetPartitionKey(ownerId) + "_Settings",
                    "Reset"),
                TableUpdateMode.Replace);
        }

        internal async Task<ChemistryHistorySnapshot>
            GetChemistryHistory(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                return new ChemistryHistorySnapshot();
            }

            var partitionKey = ChemistryHistory.GetPartitionKey(ownerId);
            var reset = await _tableClient.GetEntityIfExistsAsync<TableEntity>(
                partitionKey + "_Settings",
                "Reset");
            Expression<Func<ChemistryMatchdayEntity, bool>> filter =
                item => item.PartitionKey == partitionKey;
            if (reset.HasValue)
            {
                var resetAt = reset.Value.Timestamp
                    ?? throw new InvalidOperationException(
                        "The chemistry reset timestamp is missing.");
                filter = item => item.PartitionKey == partitionKey
                    && item.CompletedAt > resetAt;
            }
            var matchdays = new List<ChemistryMatchdayEntity>();
            await foreach (var entity in _tableClient
                .QueryAsync<ChemistryMatchdayEntity>(filter))
            {
                matchdays.Add(entity);
            }

            return new ChemistryHistorySnapshot
            {
                MatchdayCount = matchdays.Count,
                Scores = ChemistryHistory.CalculateScores(
                    matchdays,
                    DateTimeOffset.UtcNow)
            };
        }

        internal async Task<MatchdayMetadataEntity?> GetMatchdayIdentity(string partitionKey)
        {
            var response = await _tableClient.GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                partitionKey, RowKeyForStartStatus);
            return response.HasValue ? response.Value : null;
        }

        internal IAsyncEnumerable<MatchdayMetadataEntity> GetGroupMatchdaysAsync(string ownerId, string groupId, CancellationToken ct) =>
            _tableClient.QueryAsync<MatchdayMetadataEntity>(item =>
                item.RowKey == RowKeyForStartStatus && item.OwnerId == ownerId && item.GroupId == groupId,
                maxPerPage: 100, select: new[] { "PartitionKey" }, cancellationToken: ct);

        internal async Task<bool> IsClosed(string partitionKey, CancellationToken ct = default)
        {
            var entity = await _tableClient
                .GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                    partitionKey,
                    RowKeyForCloseStatus, cancellationToken: ct);
            return entity.HasValue && entity.Value.IsClosed;
        }

        internal async Task<object> GetMatchday(string partitionKey)
        {
            var entity = await _tableClient
                .GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                    partitionKey,
                    RowKeyForStartStatus);
            if (!entity.HasValue)
            {
                return new { result = "not-found" };
            }

            var entityClosed = await _tableClient
                .GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                    partitionKey,
                    RowKeyForCloseStatus);
            if (entityClosed.HasValue && entityClosed.Value.IsClosed)
            {
                return new { result = "closed" };
            }

            return new {
                result = "ok",
                ownerId = entity.Value.OwnerId,
                groupId = entity.Value.GroupId,
                teamsRevision = entity.Value.TeamsRevision,
                teams = entity.Value.SerializedTeams == null
                    ? null
                    : Newtonsoft.Json.Linq.JArray.Parse(PrivatePlayerFields.SanitizeJson(entity.Value.SerializedTeams))
            };
        }

        internal async Task<bool> StartMatchday(string partitionKey, string? ownerId = null,
            string? groupId = null, string? serializedTeams = null, string? teamsRevision = null)
        {
            if (string.IsNullOrWhiteSpace(partitionKey))
            {
                return false;
            }

            for (var attempt = 0; attempt < MaximumLifecycleAttempts; attempt++)
            {
                var startResponse = await _tableClient
                    .GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                        partitionKey,
                        RowKeyForStartStatus);
                var statusResponse = await _tableClient
                    .GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                        partitionKey,
                        RowKeyForCloseStatus);
                if (statusResponse.HasValue
                    && statusResponse.Value.IsClosed)
                {
                    return false;
                }
                if (startResponse.HasValue)
                {
                    var existing = startResponse.Value;
                    return existing.OwnerId == ownerId && existing.GroupId == groupId
                        && existing.TeamsRevision == teamsRevision
                        && existing.SerializedTeams == serializedTeams;
                }

                var start = new MatchdayMetadataEntity
                {
                    PartitionKey = partitionKey,
                    RowKey = RowKeyForStartStatus,
                    IsClosed = false,
                    OwnerId = ownerId,
                    GroupId = groupId,
                    SerializedTeams = serializedTeams,
                    TeamsRevision = teamsRevision
                };
                var actions = new List<TableTransactionAction>();
                if (statusResponse.HasValue)
                {
                    actions.Add(
                        CreateStatusGuardAction(statusResponse.Value));
                }
                else
                {
                    actions.Add(new TableTransactionAction(
                        TableTransactionActionType.Add,
                        CreateOpenStatus(partitionKey)));
                }
                actions.Add(new TableTransactionAction(
                    TableTransactionActionType.Add,
                    start));

                try
                {
                    await _tableClient.SubmitTransactionAsync(actions);
                    return true;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 412
                        || exception.Status == 404)
                {
                    continue;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 409)
                {
                    continue;
                }
            }

            return false;
        }

        internal async Task<MatchMutationResult> EditMatch(
            MatchEntity match, string? expectedRevision = null)
        {
            ArgumentNullException.ThrowIfNull(match);
            ValidateMatchIdentity(match);
            if (!HasExpectedRevision(expectedRevision))
            {
                return MatchMutationResult.UpgradeRequired;
            }

            for (var attempt = 0; attempt < MaximumLifecycleAttempts; attempt++)
            {
                var status = await GetOrCreateMatchdayStatus(
                    match.PartitionKey);
                if (status.IsClosed)
                {
                    return MatchMutationResult.Closed;
                }

                var existingResponse = await _tableClient
                    .GetEntityIfExistsAsync<MatchEntity>(
                        match.PartitionKey,
                        match.RowKey);
                if (!existingResponse.HasValue)
                {
                    return await IsClosed(match.PartitionKey)
                        ? MatchMutationResult.Closed
                        : MatchMutationResult.NotFound;
                }

                var existing = existingResponse.Value;
                if (!string.Equals(existing.Revision, expectedRevision, StringComparison.Ordinal))
                {
                    return MatchMutationResult.Conflict;
                }
                match.CreatedAt = existing.CreatedAt;
                match.ETag = existing.ETag;
                match.Timestamp = existing.Timestamp;

                try
                {
                    await _tableClient.SubmitTransactionAsync(new[]
                    {
                        CreateStatusGuardAction(status),
                        new TableTransactionAction(
                            TableTransactionActionType.UpdateReplace,
                            match,
                            existing.ETag)
                    });
                    return MatchMutationResult.Succeeded;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 412)
                {
                    var latestStatus = await GetOrCreateMatchdayStatus(
                        match.PartitionKey);
                    if (latestStatus.IsClosed)
                    {
                        return MatchMutationResult.Closed;
                    }

                    var latestMatch = await _tableClient
                        .GetEntityIfExistsAsync<MatchEntity>(
                            match.PartitionKey,
                            match.RowKey);
                    if (!latestMatch.HasValue)
                    {
                        return MatchMutationResult.NotFound;
                    }
                    if (!EtagsMatch(
                        existing.ETag,
                        latestMatch.Value.ETag))
                    {
                        return MatchMutationResult.Conflict;
                    }
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 404)
                {
                    continue;
                }
            }

            return MatchMutationResult.Conflict;
        }

        internal async Task<MatchMutationResult> DeleteMatch(
            MatchEntity match, string? expectedRevision = null)
        {
            ArgumentNullException.ThrowIfNull(match);
            ValidateMatchIdentity(match);
            if (!HasExpectedRevision(expectedRevision))
            {
                return MatchMutationResult.UpgradeRequired;
            }

            for (var attempt = 0; attempt < MaximumLifecycleAttempts; attempt++)
            {
                var status = await GetOrCreateMatchdayStatus(
                    match.PartitionKey);
                if (status.IsClosed)
                {
                    return MatchMutationResult.Closed;
                }

                var existingResponse = await _tableClient
                    .GetEntityIfExistsAsync<MatchEntity>(
                        match.PartitionKey,
                        match.RowKey);
                if (!existingResponse.HasValue)
                {
                    return await IsClosed(match.PartitionKey)
                        ? MatchMutationResult.Closed
                        : MatchMutationResult.NotFound;
                }

                var existing = existingResponse.Value;
                if (!string.Equals(existing.Revision, expectedRevision, StringComparison.Ordinal))
                {
                    return MatchMutationResult.Conflict;
                }
                try
                {
                    await _tableClient.SubmitTransactionAsync(new[]
                    {
                        CreateStatusGuardAction(status),
                        new TableTransactionAction(
                            TableTransactionActionType.Delete,
                            existing,
                            existing.ETag)
                    });
                    return MatchMutationResult.Succeeded;
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 412)
                {
                    var latestStatus = await GetOrCreateMatchdayStatus(
                        match.PartitionKey);
                    if (latestStatus.IsClosed)
                    {
                        return MatchMutationResult.Closed;
                    }

                    var latestMatch = await _tableClient
                        .GetEntityIfExistsAsync<MatchEntity>(
                            match.PartitionKey,
                            match.RowKey);
                    if (!latestMatch.HasValue)
                    {
                        return MatchMutationResult.NotFound;
                    }
                    if (!EtagsMatch(
                        existing.ETag,
                        latestMatch.Value.ETag))
                    {
                        return MatchMutationResult.Conflict;
                    }
                }
                catch (RequestFailedException exception)
                    when (exception.Status == 404)
                {
                    continue;
                }
            }

            return MatchMutationResult.Conflict;
        }

        internal async Task<bool> EditEntity<T>(T entity, string partitionKey, string rowKey)
            where T : class, ITableEntity, new()
        {
            try
            {
                var entityResponse = await _tableClient.GetEntityAsync<T>(partitionKey, rowKey);
                var entityRes = entityResponse.Value;

                // Update with original ETag for concurrency safety
                await _tableClient.UpdateEntityAsync(entity, entityRes.ETag, TableUpdateMode.Replace);

                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                return false;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
        }

        internal async Task<bool> DeleteEntity(ITableEntity entity)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, ETag.All);

                return true;
            }
            catch (RequestFailedException e)
            {
                return false;
            }

        }

        private async Task<MatchdayMetadataEntity>
            GetOrCreateMatchdayStatus(string partitionKey)
        {
            var response = await _tableClient
                .GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                    partitionKey,
                    RowKeyForCloseStatus);
            if (response.HasValue)
            {
                return response.Value;
            }

            try
            {
                await _tableClient.AddEntityAsync(
                    CreateOpenStatus(partitionKey));
            }
            catch (RequestFailedException exception)
                when (exception.Status == 409)
            {
            }

            return (await _tableClient
                .GetEntityAsync<MatchdayMetadataEntity>(
                    partitionKey,
                    RowKeyForCloseStatus))
                .Value;
        }

        private static MatchdayMetadataEntity CreateOpenStatus(
            string partitionKey)
        {
            return new MatchdayMetadataEntity
            {
                PartitionKey = partitionKey,
                RowKey = RowKeyForCloseStatus,
                IsClosed = false
            };
        }

        private static MatchdayMetadataEntity CopyStatus(
            MatchdayMetadataEntity status,
            bool isClosed)
        {
            return new MatchdayMetadataEntity
            {
                PartitionKey = status.PartitionKey,
                RowKey = status.RowKey,
                CreatedAt = status.CreatedAt == default
                    ? DateTime.UtcNow
                    : status.CreatedAt,
                ETag = status.ETag,
                Timestamp = status.Timestamp,
                IsClosed = isClosed,
                LastMatchCreatedAt = status.LastMatchCreatedAt
            };
        }

        private static TableTransactionAction CreateStatusGuardAction(
            MatchdayMetadataEntity status,
            DateTime? lastMatchCreatedAt = null)
        {
            var updatedStatus = CopyStatus(status, false);
            if (lastMatchCreatedAt.HasValue)
            {
                updatedStatus.LastMatchCreatedAt = lastMatchCreatedAt;
            }
            return new TableTransactionAction(
                TableTransactionActionType.UpdateReplace,
                updatedStatus,
                status.ETag);
        }

        private static bool EtagsMatch(ETag first, ETag second)
        {
            return string.Equals(
                first.ToString(),
                second.ToString(),
                StringComparison.Ordinal);
        }

        internal static bool HasExpectedRevision(string? revision) =>
            !string.IsNullOrWhiteSpace(revision)
            && revision.Length <= 1024
            && !revision.Contains('\r')
            && !revision.Contains('\n')
            && System.Net.Http.Headers.EntityTagHeaderValue.TryParse(revision, out var tag)
            && tag.Tag != "*";

        private static void ValidateMatchIdentity(MatchEntity match)
        {
            if (string.IsNullOrWhiteSpace(match.PartitionKey)
                || string.IsNullOrWhiteSpace(match.RowKey))
            {
                throw new ArgumentException(
                    "Matchday and match identifiers are required.");
            }
        }
    }

    internal enum MatchMutationResult
    {
        Succeeded,
        Closed,
        NotFound,
        UpgradeRequired,
        Conflict
    }

    internal sealed class MatchdayConcurrencyException : Exception
    {
        internal MatchdayConcurrencyException(string message)
            : base(message)
        {
        }
    }
}
