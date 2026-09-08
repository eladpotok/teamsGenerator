using Azure.Data.Tables;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeamsGenerator.Ai;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.Telemetry;
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
builder.Services.AddMemoryCache();
builder.Services.AddTransient<IPlayersStorageBlobConnector, PlayersStorageBlobConnector>();
builder.Services.AddTransient<ITeamsStorageBlobConnector, TeamsStorageBlobConnector>();
builder.Services.AddTransient<IUserAzureStorage, UserAzureStorage>();
builder.Services.AddSingleton<IAccountEntitlementService, AccountEntitlementService>();
builder.Services.AddSingleton<IGroupCollaborationService, GroupCollaborationService>();
builder.Services.AddSingleton<IPlayerAssessmentService, PlayerAssessmentService>();
builder.Services.AddSingleton<AzureTableStorageService>();
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new OpenAiService(
        configuration["AiApiKey"],
        configuration["AiAudience"]);
});
builder.Services.AddSingleton<IUsageTelemetry, UsageTelemetry>();


var applicationInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = applicationInsightsConnectionString;
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

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();


app.Run();