namespace TeamsGeneratorWebAPI.Groceries
{
    public static class UpdatedListProductExtensions
    {
        public static ShoppingListItemEntity ToItemEntity(this UpdatedListProductApi updatedListProductApi, string listId)
        {
            return new ShoppingListItemEntity() 
            {
                IsChecked = false,
                PartitionKey = listId,
                RowKey = updatedListProductApi.ProductId,
                Quantity = updatedListProductApi.Quantity,
                Status = "pending",
                Timestamp = DateTime.Now
            };
        }
    }
}
