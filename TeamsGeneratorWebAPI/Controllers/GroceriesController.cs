using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.Json;
using TeamsGenerator.Ai;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Utilities;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.DesignCreator;
using TeamsGeneratorWebAPI.Groceries;
using TeamsGeneratorWebAPI.PlayersBlob;

namespace TeamsGeneratorWebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GroceriesController : ControllerBase
    {
        private AzureTableGroceries _azureTableGroceries;
        private readonly TelemetryClient _telemetryClient;

        public GroceriesController(ILogger<TeamsController> logger, AzureTableGroceries azureTableGroceries, TelemetryClient telemetryClient)
        {
            _azureTableGroceries = azureTableGroceries;
            _telemetryClient = telemetryClient;
        }

        // Products

        [HttpPost("[action]")]
        public async Task<IActionResult> AddProduct(string uid, [FromBody] ProductApi product)
        {
            await _azureTableGroceries.AddProductAsync(uid, product);
            _telemetryClient.TrackMetric("ProductAdded", 1);
            return Ok(product);
        }

        [HttpPut("[action]")]
        public async Task<IActionResult> UpdateProduct(string uid, string productId, [FromBody] ProductApi product)
        {
            var response = await _azureTableGroceries.UpdateProduct(uid, productId, product);
            _telemetryClient.TrackMetric("ProductUpdated", 1);
            return Ok(response);
        }

        [HttpDelete("[action]")]
        public async Task<IActionResult> DeleteProduct(string uid, string productId)
        {
            await _azureTableGroceries.DeleteProduct(uid, productId);
            _telemetryClient.TrackMetric("ProductDeleted", 1);
            return NoContent();
        }

        [HttpPatch("[action]")]
        public async Task<IActionResult> ToggleFavorite(string uid, string productId, bool isFavorite)
        {
            var response = await _azureTableGroceries.ToggleFavorite(uid, productId, isFavorite);
            _telemetryClient.TrackMetric("ProductFavoriteToggled", 1);
            return Ok(response);
        }

        // Shopping Lists

        [HttpPost("[action]")]
        public async Task<IActionResult> CreateShoppingList(string uid, [FromBody] ShoppingListApi shoppingListApi)
        {
            var responseApi = await _azureTableGroceries.CreateShoppingList(uid, shoppingListApi);
            _telemetryClient.TrackMetric("ShoppingListCreated", 1);
            return Ok(responseApi);
        }

        [HttpPut("[action]")]
        public async Task<IActionResult> UpdateShoppingList(string uid, [FromBody] ShoppingListApi shoppingListApi)
        {
            var responseApi = await _azureTableGroceries.UpdateShoppingList(uid, shoppingListApi);
            _telemetryClient.TrackMetric("ShoppingListUpdated", 1);
            return Ok(responseApi);
        }

        [HttpDelete("[action]")]
        public async Task<IActionResult> DeleteShoppingList(string uid, string listId)
        {
            await _azureTableGroceries.DeleteShoppingList(uid, listId);
            _telemetryClient.TrackMetric("ShoppingListDeleted", 1);
            return NoContent();
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> FinalizeShoppingList(string uid, string listId)
        {
            var response = await _azureTableGroceries.FinalizeShoppingList(uid, listId);
            _telemetryClient.TrackMetric("ShoppingListDone", 1);
            return Ok(response);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> UpdateListItem(string uid, string listId, [FromBody] UpdatedListProductApi updatedListProductApi)
        {
            await _azureTableGroceries.UpdateListItem(uid, listId, updatedListProductApi);
            _telemetryClient.TrackMetric("UpdateListItem", 1);
            return NoContent();
        }

        // Categories

        [HttpPut("[action]")]
        public async Task<IActionResult> UpdateCategoryOrder(string uid, [FromBody] List<CategoryApi> categories)
        {
            await _azureTableGroceries.UpdateCategoryOrder(uid, categories);
            _telemetryClient.TrackMetric("ShoppingListOrderChanged", 1);
            return Ok();
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> AddCategory(string uid, [FromBody] CategoryApi category)
        {
            await _azureTableGroceries.AddCategory(uid, category);
            _telemetryClient.TrackMetric("CategoryAdded", 1);
            return Ok(category);
        }

        [HttpPut("[action]")]
        public async Task<IActionResult> UpdateCategory(string uid, string categoryId, [FromBody] CategoryApi category)
        {
            var categoryRes = await _azureTableGroceries.UpdateCategory(uid, categoryId, category);
            _telemetryClient.TrackMetric("CategoryUpdated", 1);
            return Ok(categoryRes);
        }

        [HttpDelete("[action]")]
        public async Task<IActionResult> DeleteCategory(string uid, string categoryId)
        {
            await _azureTableGroceries.DeleteCategory(uid, categoryId);
            _telemetryClient.TrackMetric("CategoryDeleted", 1);
            return NoContent();
        }

        // Users

        [HttpPost("[action]")]
        public async Task<IActionResult> Register([FromBody] RegisterApi register)
        {
            var registerRes = await _azureTableGroceries.Register(register);
            _telemetryClient.TrackMetric("UserRegistered", 1);
            return registerRes.Success ? Ok(registerRes) : BadRequest(registerRes);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> Login([FromBody] LoginApi login)
        {
            var registerRes = await _azureTableGroceries.Login(login);
            _telemetryClient.TrackMetric("UserLoggedIn", 1);
            return registerRes.Success ? Ok(registerRes) : BadRequest(registerRes);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> RegisterInternal([FromBody] RegisterApi register, string uid)
        {
            var registerRes = await _azureTableGroceries.Register(register, uid);
            return registerRes.Success ? Ok(registerRes) : BadRequest(registerRes);
        }


        // Global

        [HttpGet("[action]")]

        public async Task<IActionResult> FetchData(string uid)
        {
            var data = await _azureTableGroceries.FetchData(uid);
            return Ok(data);
        }


        [HttpDelete("[action]")]
        public async Task<IActionResult> DeleteAll(string uid)
        {
            await _azureTableGroceries.DeleteAll(uid);
            return NoContent();
        }
    }
}