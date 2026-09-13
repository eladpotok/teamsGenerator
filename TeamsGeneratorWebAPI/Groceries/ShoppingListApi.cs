using Azure;

namespace TeamsGeneratorWebAPI.Groceries
{
    public class ShoppingListApi
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public List<ShoppingListProductApi> Items { get; set; }

        public string CreatedAt { get; set; }

        public string UpdatedAt { get; set; }

        public bool IsDone { get; set; }

        internal static ShoppingListApi FromEntity(ShoppingListEntity shoppingListEntity, string listId)
        {
            return new ShoppingListApi() 
            {
                CreatedAt = shoppingListEntity.CreatedAt,
                UpdatedAt = shoppingListEntity.UpdatedAt,
                Id = listId,
                IsDone = shoppingListEntity.IsDone,
                Name = shoppingListEntity.Name,
            };
        }
    }
}
