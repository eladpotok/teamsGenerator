namespace TeamsGeneratorWebAPI.Groceries
{
    public class ShoppingListProductApi
    {
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public bool IsChecked { get; set; }
        public string Status { get; set; }

        internal static ShoppingListProductApi FromEntity(ShoppingListItemEntity item)
        {
            return new ShoppingListProductApi() 
            {
                IsChecked = item.IsChecked,
                ProductId = item.RowKey,
                Quantity = item.Quantity,
                Status = item.Status
            };
        }
    }
}
