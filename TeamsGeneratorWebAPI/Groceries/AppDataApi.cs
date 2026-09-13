namespace TeamsGeneratorWebAPI.Groceries
{
    public class AppDataApi
    {
        public List<ProductApi> Products { get; set; }
        public List<ShoppingListApi> ShoppingLists { get; set; }
        public List<ShoppingListProductApi> CurrentList { get; set; }
        public List<CategoryApi> Categories { get; set; }
    }
}
