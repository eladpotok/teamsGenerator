using TeamsGenerator.API;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.UsersBlob;

var builder = WebApplication.CreateBuilder(args);

WebAppAPI.Init();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddTransient<IUserConfigAzureStorage, UserConfigAzureStorage>();
builder.Services.AddTransient<IPlayersStorageBlobConnector, PlayersStorageBlobConnector>();
builder.Services.AddTransient<IUserAzureStorage, UserAzureStorage>();


var app = builder.Build();
app.UseCors(builder => {

    builder.AllowAnyOrigin();
    builder.SetIsOriginAllowed(_ => true);
    builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();


});





// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();



app.Run();
