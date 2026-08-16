using Azure.Data.Tables;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeamsGenerator.Ai;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.UsersBlob;

var builder = WebApplication.CreateBuilder(args);

WebAppAPI.Init();

// Add services to the container.

builder.Services.AddControllers();
var firebaseProjectId =
    builder.Configuration["Firebase:ProjectId"] ??
    "teamsgeneratorapp";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority =
            $"https://securetoken.google.com/{firebaseProjectId}";
        options.Audience = firebaseProjectId;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer =
                $"https://securetoken.google.com/{firebaseProjectId}",
            ValidateAudience = true,
            ValidAudience = firebaseProjectId,
            ValidateLifetime = true
        };
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Teamify API",
        Version = "v1",
        Description = "Team generation, player management, and matchday services for Teamify."
    });
});
builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddTransient<IUserConfigAzureStorage, UserConfigAzureStorage>();
builder.Services.AddTransient<IPlayersStorageBlobConnector, PlayersStorageBlobConnector>();
builder.Services.AddTransient<ITeamsStorageBlobConnector, TeamsStorageBlobConnector>();
builder.Services.AddTransient<IUserAzureStorage, UserAzureStorage>();
builder.Services.AddSingleton<GroceriesBlobStorage>();
builder.Services.AddSingleton<QuestionStorageService>();
builder.Services.AddSingleton<AzureTableGameRanker>();
builder.Services.AddSingleton<AzureTableStorageService>();
builder.Services.AddSingleton<AzureTableGroceries>();
builder.Services.AddSingleton<OpenAiService>();


builder.Services.AddApplicationInsightsTelemetry((appInsightOption) => 
{
    appInsightOption.ConnectionString = @"InstrumentationKey=a1d45916-05d0-4d09-a7a0-5f31a19ca1b6;IngestionEndpoint=https://eastus-8.in.applicationinsights.azure.com/;LiveEndpoint=https://eastus.livediagnostics.monitor.azure.com/;ApplicationId=433fb5f2-7c3c-4487-997d-7c066c2dfe50";
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();

        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy.AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<TableServiceClient>(provider =>
{
    var connectionString = builder.Configuration.GetValue<string>("BlobConnectionString");
    return new TableServiceClient(connectionString);
});

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        );
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();


app.Run();