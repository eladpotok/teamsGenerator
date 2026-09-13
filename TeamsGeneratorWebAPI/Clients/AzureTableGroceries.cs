using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;
using TeamsGeneratorWebAPI.Groceries;

namespace TeamsGeneratorWebAPI.Clients
{
    public class AzureTableGroceries
    {
        private readonly TableClient _tableClient;
        private readonly GroceriesBlobStorage _groceriesBlobStorage;
        private const string ShoppingListPrefix = "list_";
        private const string ProductsPrefix = "product_";
        private const string ProductsInShoppingListPrefix = "productInList_";
        private const string CategoryPrefix = "category_";

        public AzureTableGroceries(TableServiceClient tableServiceClient, GroceriesBlobStorage groceriesBlobStorage)
        {
            // This will create the table if it doesn't exist
            _tableClient = tableServiceClient.GetTableClient("Groceries");
            _tableClient.CreateIfNotExists();
            _groceriesBlobStorage = groceriesBlobStorage;
        }

        private static string GetShoppingListId(string uid)
        {
            return $"{ShoppingListPrefix}_{uid}";
        }

        private static string GetProductId(string uid)
        {
            return $"{ProductsPrefix}_{uid}";
        }

        private static string GetProductsInListId(string uid)
        {
            return $"{ProductsInShoppingListPrefix}_{uid}";
        }

        private static string GetCategoryId(string uid)
        {
            return $"{CategoryPrefix}_{uid}";
        }

        public async Task AddProductAsync(string uid, ProductApi product)
        {
            try
            {
                var productEntity = ProductEntity.FromApi(product, GetProductId(uid));

                if (productEntity.HasImage)
                {
                    var blobId = await _groceriesBlobStorage.UploadImage(product.ImageBase64, product.Id);
                }

                await _tableClient.AddEntityAsync(productEntity);
            }
            catch (Exception e)
            {

                throw;
            }
        }

        internal async Task DeleteProduct(string uid, string productId)
        {
            await _tableClient.DeleteEntityAsync(GetProductId(uid), productId, ETag.All);
        }

        internal async Task<ProductApi> UpdateProduct(string uid, string productId, ProductApi product)
        {
            var productEntity = ProductEntity.FromApi(product, GetProductId(uid));
            if (productEntity.HasImage)
            {
                var blobId = await _groceriesBlobStorage.UploadImage(product.ImageBase64, productId);
            }

            await EditEntity(productEntity, GetProductId(uid), productId);
            return product;
        }

        internal async Task<ProductFavoriteResponseApi> ToggleFavorite(string uid, string productId, bool isFavorite)
        {
            var productEntity = await _tableClient.GetEntityAsync<ProductEntity>(GetProductId(uid), productId);
            productEntity.Value.IsFavorite = isFavorite;
            await EditEntity(productEntity.Value, GetProductId(uid), productId);
            return new ProductFavoriteResponseApi() 
            {
                isFavorite = isFavorite,
                ProductId = productId
            };
        }

        internal async Task<ShoppingListApi> FinalizeShoppingList(string uid, string listId)
        {
            var shoppingListEntity = await _tableClient.GetEntityAsync<ShoppingListEntity>(GetShoppingListId(uid), listId);
            shoppingListEntity.Value.IsDone = true;

            var shoppingListApi = ShoppingListApi.FromEntity(shoppingListEntity.Value, listId);
            var allItems = await GetAllEntities<ShoppingListItemEntity>(GetProductsInListId(listId));
            var allItemsAsProducts = allItems.Select(item => { return ShoppingListProductApi.FromEntity(item); });
            shoppingListApi.Items = allItemsAsProducts.ToList();

            await EditEntity(shoppingListEntity.Value, uid, listId);

            return shoppingListApi;
        }

        internal async Task UpdateListItem(string uid, string listId, UpdatedListProductApi updatedListProductApi)
        {
            try
            {
                var operation = updatedListProductApi.Operation;

                if (operation == "add")
                {
                    var listProductEntity = updatedListProductApi.ToItemEntity(GetProductsInListId(listId));
                    await _tableClient.AddEntityAsync(listProductEntity);
                }
                else if (operation == "update")
                {
                    var listProductEntity = await _tableClient.GetEntityAsync<ShoppingListItemEntity>(GetProductsInListId(listId), updatedListProductApi.ProductId);
                    var listProductEntityValue = listProductEntity.Value;
                    listProductEntityValue.Quantity = updatedListProductApi.Quantity;
                    await EditEntity(listProductEntityValue, listProductEntityValue.PartitionKey, listProductEntityValue.RowKey);
                }
                else if (operation == "remove")
                {
                    await _tableClient.DeleteEntityAsync(GetProductsInListId(listId), updatedListProductApi.ProductId, ETag.All);
                }
            }
            catch (Exception e)
            {
                
            }
        }

        internal async Task UpdateCategoryOrder(string uid, List<CategoryApi> categories)
        {
            foreach (var categoryApi in categories)
            {
                var categoryEntity = await _tableClient.GetEntityAsync<CategoryEntity>(GetCategoryId(uid), categoryApi.Id);
                categoryEntity.Value.SupermarketOrder = categoryApi.SupermarketOrder;
                await EditEntity<CategoryEntity>(categoryEntity.Value, GetCategoryId(uid), categoryApi.Id);
            }
        }

        internal async Task<RegisterResponseApi> Register(RegisterApi register, string uid = null)
        {
            try
            {
                var username = register.GetUsername();
                var existingUser = await EntityExistsByPropertyAsync("Name", username);
                if (existingUser)
                {
                    return new RegisterResponseApi()
                    {
                        Error = "This username is already in use. Please choose another",
                        Success = false,
                    };
                }

                var registerEntity = RegisterEntity.FromApi(register);
                registerEntity.RowKey = uid ?? registerEntity.RowKey;
                await _tableClient.AddEntityAsync(registerEntity);
                return new RegisterResponseApi()
                {
                    Success = true,
                    UserId = registerEntity.RowKey
                };
            }
            catch (Exception e) 
            {
                return new RegisterResponseApi()
                {
                    Error = e.Message,
                    Success = false,
                };
            }
        }

        internal async Task<LoginResponseApi> Login(LoginApi login)
        {
            try
            {
                string filter = TableClient.CreateQueryFilter($"Name eq {login.Username.ToLower()}");
                await foreach (var entity in _tableClient.QueryAsync<RegisterEntity>(filter))
                {
                    var isPassCorrect = Utils.VerifyHashedPassword(entity.Password, login.Password);
                    if (!isPassCorrect)
                    {
                        return new LoginResponseApi()
                        {
                            Error = "The password you entered is incorrect",
                            Success = false
                        };
                    }

                    return new LoginResponseApi() 
                    {
                        Success = true,
                        UserId = entity.RowKey,
                        Username = entity.Name
                    };
                }
            }
            catch (Exception e) 
            {
                return new LoginResponseApi()
                {
                    Error = e.Message,
                    Success = false
                };
            }

            return new LoginResponseApi()
            {
                Error = "No user with the given username exists",
                Success = false
            };
        }

        internal async Task<CategoryApi> UpdateCategory(string uid, string categoryId, CategoryApi category)
        {
            var categoryEntity = CategoryEntity.FromApi(category, GetCategoryId(uid));
            await EditEntity(categoryEntity, GetCategoryId(uid), categoryId);
            return category;
        }

        internal async Task DeleteCategory(string uid, string categoryId)
        {
            await _tableClient.DeleteEntityAsync(GetCategoryId(uid), categoryId, ETag.All);
        }

        internal async Task AddCategory(string uid, CategoryApi category)
        {
            try
            {
                var categoryEntity = CategoryEntity.FromApi(category, GetCategoryId(uid));
                await _tableClient.AddEntityAsync(categoryEntity);
            }
            catch (Exception e)
            {

            }
        }

        internal async Task DeleteShoppingList(string uid, string listId)
        {
            await _tableClient.DeleteEntityAsync(GetShoppingListId(uid), listId, ETag.All);
        }

        internal async Task<ShoppingListApi> UpdateShoppingList(string uid, ShoppingListApi shoppingListApi)
        {
            var shoppingListEntity = ShoppingListEntity.FromApi(shoppingListApi, GetShoppingListId(uid));
            await EditEntity<ShoppingListEntity>(shoppingListEntity, shoppingListEntity.PartitionKey, shoppingListEntity.RowKey);

            // Delete all items of the current shopping list
            //await _tableClient.DeleteEntityAsync(GetProductsInListId(shoppingListApi.Id), shoppingListApi.Id, ETag.All);

            await foreach (var entity in _tableClient.QueryAsync<ShoppingListItemEntity>(e => e.PartitionKey == GetProductsInListId(shoppingListApi.Id)))
            {
                // 2. Delete each entity
                await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, ETag.All);
            }

            foreach (var item in shoppingListApi.Items)
            {
                try
                {
                    var shoppingListItemEntitiy = ShoppingListItemEntity.FromApi(item, GetProductsInListId(shoppingListApi.Id));
                    await _tableClient.AddEntityAsync(shoppingListItemEntitiy);
                }
                catch (Exception e)
                {
                    
                }
            }

            return shoppingListApi;
        }

        internal async Task<ShoppingListApi> CreateShoppingList(string uid, ShoppingListApi shoppingListApi)
        {
            var shoppingListEntity = ShoppingListEntity.FromApi(shoppingListApi, GetShoppingListId(uid));
            await _tableClient.AddEntityAsync(shoppingListEntity);

            foreach (var item in shoppingListApi.Items)
            {
                var shoppingListItemEntitiy = ShoppingListItemEntity.FromApi(item, GetProductsInListId(shoppingListApi.Id));
                await _tableClient.AddEntityAsync(shoppingListItemEntitiy);
            }

            return shoppingListApi;
        }

        internal async Task DeleteAll(string uid)
        {
            await _tableClient.DeleteAsync();
        }

        public async Task<List<ProductApi>> GetAllProducts(string uid)
        {
            try
            {
                var matches = new List<ProductApi>();
                await foreach (var entity in _tableClient.QueryAsync<ProductEntity>((e => e.PartitionKey == GetProductId(uid))))
                {
                    var productApi = new ProductApi()
                    {
                        Category = entity.CategoryName,
                        Name = entity.Name,
                        Id = entity.RowKey,
                        CreatedAt = entity.Timestamp.ToString(),
                        IsFavorite = entity.IsFavorite,
                        WeightUnit = entity.WeightUnit,
                        MeasurementType = entity.MeasurementType,
                        Description = entity.Description,
                    };

                    if (entity.HasImage)
                    {
                        var image64Base = await _groceriesBlobStorage.DownloadImageAsync(entity.RowKey);
                        productApi.ImageBase64 = image64Base;
                    }

                    matches.Add(productApi);
                }

                return matches;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public async Task<List<CategoryApi>> GetAllCategories(string uid)
        {
            try
            {
                var matches = new List<CategoryApi>();
                await foreach (var entity in _tableClient.QueryAsync<CategoryEntity>((e => e.PartitionKey == GetCategoryId(uid))))
                {
                    matches.Add(new CategoryApi()
                    {
                        Name = entity.Name,
                        Id = entity.RowKey,
                        SupermarketOrder = entity.SupermarketOrder,
                        Color = entity.Color,
                    });
                }

                return matches;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public async Task<List<ShoppingListApi>> GetShoppingLists(string uid)
        {
            try
            {
                var shoppingLists = new List<ShoppingListApi>();
                await foreach (var entity in _tableClient.QueryAsync<ShoppingListEntity>((e => e.PartitionKey == GetShoppingListId(uid))))
                {
                    var shoppingList = new ShoppingListApi()
                    {
                        Name = entity.Name,
                        Id = entity.RowKey,
                        CreatedAt = entity.CreatedAt,
                        UpdatedAt = entity.UpdatedAt,
                        IsDone = entity.IsDone
                    };

                    var itemsInShoppingList = await GetAllEntities<ShoppingListItemEntity>(GetProductsInListId(shoppingList.Id));
                    shoppingList.Items = itemsInShoppingList.Select(t => new ShoppingListProductApi() 
                    {
                        IsChecked = t.IsChecked,
                        ProductId = t.RowKey,
                        Quantity = t.Quantity,
                        Status = t.Status
                    }).ToList();

                    shoppingLists.Add(shoppingList);
                }

                return shoppingLists;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        

        public async Task<AppDataApi> FetchData(string uid)
        {
            try
            {
                var products = await GetAllProducts(uid);
                var shoppingLists = await GetShoppingLists(uid);
                var categories = await GetAllCategories(uid);

                return new AppDataApi()
                {
                    Products = products ?? new List<ProductApi>(),
                    ShoppingLists = shoppingLists ?? new List<ShoppingListApi>(),
                    Categories = categories ?? new List<CategoryApi>()
                };
            }
            catch (Exception e)
            {
                return null;
            }
        }

        private async Task<List<T>> GetAllEntities<T>(string partitionKey, CancellationToken cancellationToken = default)
                where T : class, ITableEntity, new()
        {
            var matches = new List<T>();
            await foreach (var entity in _tableClient.QueryAsync<T>(e => e.PartitionKey == partitionKey, cancellationToken: cancellationToken))
            {
                matches.Add(entity);
            }
            return matches.OrderByDescending(t => t.Timestamp).ToList();
        }

        private async Task<bool> EditEntity<T>(T entity, string partitionKey, string rowKey)
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

        private async Task<bool> EntityExistsByPropertyAsync(string propertyName, string propertyValue)
        {
            // Build the query filter
            string filter = TableClient.CreateQueryFilter($"Name eq {propertyValue}");

            // Query only the first page to avoid unnecessary reads
            await foreach (var entity in _tableClient.QueryAsync<RegisterEntity>(filter))
            {
                return true; // Found at least one
            }

            return false; // No matching entity
        }
    }
}

