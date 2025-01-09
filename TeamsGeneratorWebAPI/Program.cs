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

builder.Services.AddApplicationInsightsTelemetry((appInsightOption) => 
{
    appInsightOption.ConnectionString = @"InstrumentationKey=a1d45916-05d0-4d09-a7a0-5f31a19ca1b6;IngestionEndpoint=https://eastus-8.in.applicationinsights.azure.com/;LiveEndpoint=https://eastus.livediagnostics.monitor.azure.com/;ApplicationId=433fb5f2-7c3c-4487-997d-7c066c2dfe50";
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", policy =>
    {
        policy.WithOrigins("http://localhost:3001")  // Allow your frontend origin
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});


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

app.UseCors("AllowLocalhost");


app.Run();
