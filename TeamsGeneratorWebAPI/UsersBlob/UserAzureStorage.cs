using Azure.Storage.Blobs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.UsersBlob
{
    public interface IUserAzureStorage : IAzureStorage { }

    public class UserAzureStorage : IUserAzureStorage
    {
        private readonly string _storageConnectionString;
        private readonly string _storageContainerName;

        public UserAzureStorage(IConfiguration configuration)
        {
            _storageConnectionString = configuration.GetValue<string>("BlobConnectionString");
            _storageContainerName = configuration.GetValue<string>("UsersBlobContainerName");
        }

        public Task<IResponse> DeleteAsync(string blobFilename)
        {
            throw new NotImplementedException();
        }

        public Task<IResponse> DownloadAsync(string blobFilename)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Login
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public async Task<IResponse> ListAsync(IConfig config)
        {
            try
            {
                var user = config as UserBlobConfig;

                var userArgs = JsonConvert.DeserializeObject<LoginUserArgs>(user.User.ToString());
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);

                BlobClient client = container.GetBlobClient($"{userArgs.Email}_user");
                if (client == null) return LoginResponse.Failure("1");

                if (await client.ExistsAsync())
                {
                    var data = await client.OpenReadAsync();
                    Stream blobContent = data;

                    var content = await client.DownloadContentAsync();
                    var value = content.Value;
                    var json = value.Content;
                    var serializedUser = JsonConvert.DeserializeObject<UserContainer>(json.ToString());
                    
                    if(Utils.VerifyHashedPassword(serializedUser.Password, userArgs.Password))
                    {
                        return new LoginResponse(new LoginUserResponse()
                        {
                            Id = serializedUser.Id,
                            Name = serializedUser.Name,
                            UserCreatedTime = serializedUser.UserCreatedTime
                        });
                    }

                }
                
                return LoginResponse.Failure("1");
            }
            catch (Exception e)
            {
                return LoginResponse.Failure(e.Message);
            }

            return LoginResponse.Failure("3");
        }

        /// <summary>
        /// Sign in
        /// </summary>
        /// <param name="data"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<IResponse> UploadAsync(dynamic data, IConfig config)
        {
            var user = config as UserBlobConfig;
            var userArgs = JsonConvert.DeserializeObject<SignUserArgs>(user.User.ToString());
            BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);
            BlobClient client = container.GetBlobClient($"{userArgs.Email}_user");
            var isExists = await client.ExistsAsync();
            if(isExists)
            {
                return LoginResponse.Failure("4");
            }
            

            var dataToSave = new UserContainer()
            {
                Id = Guid.NewGuid().ToString(),
                Name = userArgs.Name,
                Password = Utils.HashPassword(userArgs.Password),
                UserCreatedTime = DateTime.Now
            };

            JObject jobject = JObject.FromObject(dataToSave);
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(jobject.ToString())))
            {
                await client.UploadAsync(ms, overwrite: true);
            }

            var response = new LoginResponse(new LoginUserResponse()
            {
                Id = dataToSave.Id,
                Name = dataToSave.Name,
                UserCreatedTime = dataToSave.UserCreatedTime
            });

            return response;
        }
    }
}
