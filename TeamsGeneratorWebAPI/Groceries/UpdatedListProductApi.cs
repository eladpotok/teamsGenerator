namespace TeamsGeneratorWebAPI.Groceries
{
    public class UpdatedListProductApi
    {
        public string ProductId { get; set; }
        public string Operation { get; set; } // 'add' | 'update' | 'remove'
        public int Quantity { get; set; }
    }
}
