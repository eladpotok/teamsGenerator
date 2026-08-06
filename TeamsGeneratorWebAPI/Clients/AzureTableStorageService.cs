using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Clients
{
    public class AzureTableStorageService
    {
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

        public async Task AddMatchAsync(MatchEntity match)
        {
            try
            {
                await _tableClient.AddEntityAsync(match);
            }
            catch (Exception e)
            {

                throw;
            }
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
            try
            {
                var matches = new List<MatchEntity>();
                await foreach (var entity in _tableClient.QueryAsync<MatchEntity>((e => e.PartitionKey == partitionKey)))
                {
                    matches.Add(entity);
                }


                return matches.Where(row => !MatchesForStatusKeys.Contains(row.RowKey)).OrderBy(t => t.CreatedAt).ToList();
            }
            catch (Exception e)
            {
                return null;
            }
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

        internal async Task DoneMatch(MatchdayMetadataEntity match)
        {
            await _tableClient.AddEntityAsync(match);
        }

        internal async Task<bool> IsClosed(string partitionKey)
        {
            try
            {
                var entity = await _tableClient.GetEntityAsync<MatchdayMetadataEntity>(partitionKey: partitionKey, rowKey: RowKeyForCloseStatus);
                if (entity == null) return false;

                return entity.Value.IsClosed;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        internal async Task<object> GetMatchday(string partitionKey)
        {
            try
            {
                var entity = await _tableClient.GetEntityAsync<MatchdayMetadataEntity>(partitionKey: partitionKey, rowKey: RowKeyForStartStatus);
                if (entity == null)
                {
                    return new { result = "not-found" };
                }
                var entityClosed = await _tableClient.GetEntityIfExistsAsync<MatchdayMetadataEntity>(partitionKey: partitionKey, rowKey: RowKeyForCloseStatus);
                if (entityClosed.HasValue)
                {
                    return new { result = "closed" };
                }

                return new { result = "ok" };
            }
            catch (Exception ex)
            {
                return new { result = "not-found" };
            }
        }

        internal async Task<bool> EditMatch(MatchEntity match)
        {
            try
            {
                var entityResponse = await _tableClient.GetEntityAsync<MatchEntity>(match.PartitionKey, match.RowKey);
                var entity = entityResponse.Value;

                // Update with original ETag for concurrency safety
                await _tableClient.UpdateEntityAsync(match, entity.ETag, TableUpdateMode.Replace);

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
    }
}
