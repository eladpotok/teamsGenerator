using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Clients
{
    public class AzureTableStorageService
    {
        private const int MaximumLifecycleAttempts = 5;
        private readonly TableClient _tableClient;

        public static string RowKeyForCloseStatus = "ReservedToStatus";
        public static string RowKeyForStartStatus = "ReservedToStatusStart";

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

        internal async Task<MatchMutationResult> AddMatchAsync(
            MatchEntity match)
        {
            ArgumentNullException.ThrowIfNull(match);
            ValidateMatchIdentity(match);

            match.CreatedAt = DateTime.UtcNow;
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

                try
                {
                    await _tableClient.SubmitTransactionAsync(new[]
                    {
                        CreateStatusGuardAction(status),
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

        public async Task<List<MatchEntity>> GetAllMatchesAsync(string partitionKey)
        {
            var matches = new List<MatchEntity>();
            await foreach (var entity in _tableClient
                .QueryAsync<MatchEntity>(
                    e => e.PartitionKey == partitionKey))
            {
                matches.Add(entity);
            }

            return matches
                .Where(row => !MatchesForStatusKeys.Contains(row.RowKey))
                .OrderBy(match => match.CreatedAt)
                .ToList();
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

        internal async Task<IReadOnlyDictionary<string, double>>
            GetChemistryScores(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                return new Dictionary<string, double>();
            }

            var partitionKey = ChemistryHistory.GetPartitionKey(ownerId);
            var matchdays = new List<ChemistryMatchdayEntity>();
            await foreach (var entity in _tableClient
                .QueryAsync<ChemistryMatchdayEntity>(
                    item => item.PartitionKey == partitionKey))
            {
                matchdays.Add(entity);
            }

            return ChemistryHistory.CalculateScores(
                matchdays,
                DateTimeOffset.UtcNow);
        }

        internal async Task<bool> IsClosed(string partitionKey)
        {
            var entity = await _tableClient
                .GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                    partitionKey,
                    RowKeyForCloseStatus);
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

            return new { result = "ok" };
        }

        internal async Task<bool> StartMatchday(string partitionKey)
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
                if (startResponse.HasValue)
                {
                    return false;
                }

                var statusResponse = await _tableClient
                    .GetEntityIfExistsAsync<MatchdayMetadataEntity>(
                        partitionKey,
                        RowKeyForCloseStatus);
                if (statusResponse.HasValue
                    && statusResponse.Value.IsClosed)
                {
                    return false;
                }

                var start = new MatchdayMetadataEntity
                {
                    PartitionKey = partitionKey,
                    RowKey = RowKeyForStartStatus,
                    IsClosed = false
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

        internal async Task<MatchMutationResult> EditMatch(MatchEntity match)
        {
            ArgumentNullException.ThrowIfNull(match);
            ValidateMatchIdentity(match);

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
            MatchEntity match)
        {
            ArgumentNullException.ThrowIfNull(match);
            ValidateMatchIdentity(match);

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
                IsClosed = isClosed
            };
        }

        private static TableTransactionAction CreateStatusGuardAction(
            MatchdayMetadataEntity status)
        {
            return new TableTransactionAction(
                TableTransactionActionType.UpdateReplace,
                CopyStatus(status, false),
                status.ETag);
        }

        private static bool EtagsMatch(ETag first, ETag second)
        {
            return string.Equals(
                first.ToString(),
                second.ToString(),
                StringComparison.Ordinal);
        }

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
