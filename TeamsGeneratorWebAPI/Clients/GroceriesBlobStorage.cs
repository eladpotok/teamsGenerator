using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.Clients
{
    public class GroceriesBlobStorage 
    {
        private readonly string _storageConnectionString;
        private readonly string _storageContainerName;
        private readonly ILogger<GroceriesBlobStorage> _logger;

        public GroceriesBlobStorage(IConfiguration configuration, ILogger<GroceriesBlobStorage> logger)
        {
            _storageConnectionString = configuration.GetValue<string>("BlobConnectionString");
            _storageContainerName = configuration.GetValue<string>("UserConfigBlobContainerName");
            _logger = logger;
        }

        public async Task<string> DownloadImageAsync(string blobName)
        {
            var blobServiceClient = new BlobServiceClient(_storageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient("base64storage");

            var blobClient = containerClient.GetBlobClient($"{blobName}.txt");

            var download = await blobClient.DownloadContentAsync();

            return download.Value.Content.ToString();
        }

        public async Task<string> UploadImage(string base64Image, string productId)
        {
            var blobServiceClient = new BlobServiceClient(_storageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient("base64storage");

            await containerClient.CreateIfNotExistsAsync();

            string blobName = $"{productId}.txt";

            var blobClient = containerClient.GetBlobClient(blobName);

            // Convert string to bytes (UTF-8)
            byte[] bytes = Encoding.UTF8.GetBytes(base64Image);

            using var stream = new MemoryStream(bytes);

            await blobClient.UploadAsync(stream, overwrite: true);

            return blobName; // store this in table
        }
    }
}
