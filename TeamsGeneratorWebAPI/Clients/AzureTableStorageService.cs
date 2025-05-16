using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Clients
{
    public class AzureTableStorageService
    {
        private readonly TableClient _tableClient;

        public static string RowKeyForCloseStatus = "ReservedToStatus";

        public AzureTableStorageService(TableServiceClient tableServiceClient)
        {
            // This will create the table if it doesn't exist
            _tableClient = tableServiceClient.GetTableClient("Matches");
            _tableClient.CreateIfNotExists();
        }

        public async Task AddMatchAsync(MatchEntity match)
        {
            await _tableClient.AddEntityAsync(match);
        }

        public async Task<List<MatchEntity>> GetAllMatchesAsync(string partitionKey)
        {
            var matches = new List<MatchEntity>();
            await foreach (var entity in _tableClient.QueryAsync<MatchEntity>((e => e.PartitionKey == partitionKey && e.RowKey != RowKeyForCloseStatus)))
            {
                matches.Add(entity);
            }
            return matches.OrderBy(t => t.CreatedAt).ToList();
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

        internal async Task<bool> DeleteMatch(MatchEntity match)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(match.PartitionKey, match.RowKey, ETag.All);

                return true;
            }
            catch (RequestFailedException e) 
            {
                return false;
            }
           
        }

    }
}
