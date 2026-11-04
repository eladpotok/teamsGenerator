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
using TeamsGeneratorWebAPI.Capacity;
using Microsoft.Extensions.Logging.AzureAppServices;
using TeamsGeneratorWebAPI.OwnerEmail;
using TeamsGeneratorWebAPI.Polls;
using TeamsGeneratorWebAPI.Announcements;

var builder = WebApplication.CreateBuilder(args);
// Capability URLs must not enter the default hosting request-start/request-end logs.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

WebAppAPI.Init();

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddRuntimeCapacity(builder.Configuration);
builder.Services.AddPremiumBilling(builder.Configuration);
builder.Services.AddOwnerEmail(builder.Configuration);
builder.Services.AddAccountAnnouncements(builder.Configuration);
builder.Logging.AddFilter("System.Net.Http.HttpClient.OwnerEmail", LogLevel.None);
// Opaque billing customer IDs are credentials, not telemetry dimensions.
builder.Logging.AddFilter("System.Net.Http.HttpClient.RevenueCat", LogLevel.None);
builder.Logging.AddFilter("System.Net.Http.HttpClient.GooglePlay", LogLevel.None);
if (builder.Configuration.GetValue<bool>("Billing:WorkerFileLoggingEnabled"))
{
    builder.Logging.AddAzureWebAppDiagnostics();
    builder.Services.Configure<AzureFileLoggerOptions>(options =>
    {
        options.FileName = "teamify-billing-worker-";
        options.FileSizeLimit = 16 * 1024 * 1024;
        options.RetainedFileCountLimit = 3;
        options.IncludeScopes = false;
    });
    builder.Services.Configure<LoggerFilterOptions>(options =>
    {
        options.Rules.Add(new LoggerFilterRule("AzureAppServicesFile", null, null, (_, category, level) =>
            category == typeof(BillingWorker).FullName && level >= LogLevel.Information));
        options.Rules.Add(new LoggerFilterRule("AzureAppServicesBlob", null, LogLevel.None, null));
    });
}
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
            OnTokenValidated = async context =>
            {
                await context.HttpContext.RequestServices.GetRequiredService<IPremiumAccountStore>()
                    .UpdateVerifiedIdentityAsync(context.Principal!, context.HttpContext.RequestAborted);
                var token = context.SecurityToken switch
                {
                    Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt => jwt.EncodedToken,
                    System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt => jwt.RawData,
                    _ => string.Empty
                };
                await context.HttpContext.RequestServices.GetRequiredService<AuthenticatedAccountObserver>()
                    .ObserveAsync(context.Principal!, token,
                        context.Request.Headers["client_version"].ToString(), context.HttpContext.RequestAborted);
            }
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
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<IPlayersStorageBlobConnector, PlayersStorageBlobConnector>();
builder.Services.AddTransient<ITeamsStorageBlobConnector, TeamsStorageBlobConnector>();
builder.Services.AddTransient<IUserAzureStorage, UserAzureStorage>();
builder.Services.AddSingleton<IAccountEntitlementService, AccountEntitlementService>();
builder.Services.AddSingleton<IPremiumAccountStore, PremiumAccountStore>();
builder.Services.AddSingleton<IGroupCollaborationService, GroupCollaborationService>();
builder.Services.AddSingleton<IPlayerAssessmentService, PlayerAssessmentService>();
builder.Services.AddSingleton<AzureTableStorageService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<TablePollStore>();
builder.Services.AddSingleton<PollService>();
builder.Services.AddSingleton<PollPublicRateLimiter>();
builder.Services.AddScoped<PollExceptionFilter>();
builder.Services.AddSingleton<ITelemetryInitializer, PollTelemetryRedactor>();
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
builder.Services.AddSingleton<WebsiteVisitRateLimiter>();
builder.Services.AddSingleton<IRegistrationStore, RegistrationStore>();
builder.Services.AddSingleton<AccountObservationStore>();
builder.Services.AddSingleton<AuthenticatedAccountObserver>();
builder.Services.AddSingleton<IFirebaseAccountLookup, FirebaseAccountLookup>();
builder.Services.AddHttpClient(FirebaseAccountLookup.ClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Logging.AddFilter("System.Net.Http.HttpClient." + FirebaseAccountLookup.ClientName, LogLevel.None);
builder.Services.AddSingleton<ITelemetryInitializer, FirebaseAccountTelemetryRedactor>();
builder.Services.AddSingleton<IInvitationDocumentStore, BlobInvitationDocumentStore>();
builder.Services.AddSingleton<IInvitationClock, InvitationClock>();
builder.Services.AddSingleton<IInvitationGroupSource, InvitationGroupSource>();
builder.Services.AddSingleton<InvitationService>();
builder.Services.AddSingleton<TeamsGeneratorWebAPI.PlayerProfiles.PlayerProfileService>();
builder.Services.AddSingleton<TeamsGeneratorWebAPI.PlayerProfiles.PlayerMembershipService>();
builder.Services.AddSingleton<TeamsGeneratorWebAPI.PlayerProfiles.IPlayerPerformanceSource, TeamsGeneratorWebAPI.PlayerProfiles.PlayerPerformanceSource>();
builder.Services.AddSingleton<TeamsGeneratorWebAPI.PlayerProfiles.PlayerPerformanceService>();
builder.Services.AddSingleton<TeamsGeneratorWebAPI.GroupJoins.GroupJoinService>();
builder.Services.AddSingleton<TeamsGeneratorWebAPI.GroupJoins.GroupJoinRoster>();
builder.Services.AddSingleton<TeamsGeneratorWebAPI.GroupJoins.IGroupJoinAnalyzer, TeamsGeneratorWebAPI.GroupJoins.GroupJoinAnalyzer>();
builder.Services.AddSingleton<NotificationAccountService>();
builder.Services.AddSingleton<InvitationPublicRateLimiter>();
builder.Services.AddScoped<InvitationExceptionFilter>();
builder.Services.AddSingleton<FirebaseDigestSender>();
builder.Services.AddSingleton<IInvitationPushSender>(services => services.GetRequiredService<FirebaseDigestSender>());
builder.Services.AddSingleton<IInvitationPromotionPushSender>(services => services.GetRequiredService<FirebaseDigestSender>());
builder.Services.AddSingleton<InvitationDigestProcessor>();
builder.Services.AddSingleton<InvitationPromotionProcessor>();
if (builder.Configuration.GetValue<bool>("Notifications:Fcm:Enabled")
    && builder.Configuration.GetValue("Notifications:Digests:Enabled", true))
{
    builder.Services.AddHostedService<InvitationDigestWorker>();
}
if (builder.Configuration.GetValue<bool>("Notifications:Fcm:Enabled")
    && builder.Configuration.GetValue<bool>("Notifications:Promotions:Enabled"))
{
    builder.Services.AddHostedService<InvitationPromotionWorker>();
}
builder.Services.AddSingleton<ITelemetryInitializer, InvitationTelemetryRedactor>();
builder.Services.AddSingleton<ITelemetryInitializer, BillingTelemetryRedactor>();


var applicationInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = applicationInsightsConnectionString;
    options.EnableAdaptiveSampling = false;
});
builder.Services.Configure<TelemetryConfiguration>(configuration =>
{
    // Keep product events for distinct-user and latest-preference reporting.
    configuration.DefaultTelemetrySink.TelemetryProcessorChainBuilder
        .UseAdaptiveSampling(maxTelemetryItemsPerSecond: 5, excludedTypes: "Event")
        .Build();
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
            .AllowAnyMethod()
            .WithExposedHeaders("Retry-After");
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
TeamsGeneratorWebAPI.ApplicationHealth.UseDeploymentIdentity(app);

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
app.UseMiddleware<RuntimeCapacityMiddleware>();

app.MapGet("/health", TeamsGeneratorWebAPI.ApplicationHealth.Read);
app.MapControllers();


app.Run();