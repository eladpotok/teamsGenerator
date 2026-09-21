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
using TeamsGeneratorWebAPI.Invitations;
using Microsoft.ApplicationInsights.Extensibility;

var builder = WebApplication.CreateBuilder(args);
// Capability URLs must not enter the default hosting request-start/request-end logs.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

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
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context => context.HttpContext.RequestServices
                .GetRequiredService<IPremiumAccountStore>()
                .UpdateVerifiedIdentityAsync(context.Principal!, context.HttpContext.RequestAborted)
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
builder.Services.AddSingleton<IPremiumAccountStore, PremiumAccountStore>();
builder.Services.AddSingleton<IGroupCollaborationService, GroupCollaborationService>();
builder.Services.AddSingleton<IPlayerAssessmentService, PlayerAssessmentService>();
builder.Services.AddSingleton<AzureTableStorageService>();
builder.Services.AddSingleton<AzureTableGroceries>();
builder.Services.AddSingleton<GroceriesBlobStorage>();
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new Lazy<OpenAiService>(() =>
        new OpenAiService(
            configuration["AiApiKey"],
            configuration["AiAudience"]));
});
builder.Services.AddSingleton<IUsageTelemetry, UsageTelemetry>();
builder.Services.AddSingleton<IInvitationDocumentStore, BlobInvitationDocumentStore>();
builder.Services.AddSingleton<IInvitationClock, InvitationClock>();
builder.Services.AddSingleton<IInvitationGroupSource, InvitationGroupSource>();
builder.Services.AddSingleton<InvitationService>();
builder.Services.AddSingleton<NotificationAccountService>();
builder.Services.AddSingleton<InvitationPublicRateLimiter>();
builder.Services.AddScoped<InvitationExceptionFilter>();
builder.Services.AddSingleton<IInvitationPushSender, FirebaseDigestSender>();
builder.Services.AddSingleton<InvitationDigestProcessor>();
if (builder.Configuration.GetValue<bool>("Notifications:Fcm:Enabled"))
{
    builder.Services.AddHostedService<InvitationDigestWorker>();
}
builder.Services.AddSingleton<ITelemetryInitializer, InvitationTelemetryRedactor>();


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