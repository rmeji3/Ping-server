using System.Text;
using Prometheus;
using Prometheus.DotNetRuntime;
using Amazon.S3;
using Conquest.Data.Auth;
using Conquest.Data.App;
using Conquest.Dtos.Auth;
using Conquest.Features.Auth;
using Conquest.Models.AppUsers;
using Conquest.Services.Friends;
using Conquest.Services.Places;
using Conquest.Services.Events;
using Conquest.Services.Reviews;
using Conquest.Services.Business;
using Conquest.Services.Tags;
using Conquest.Services.Activities;
using Conquest.Services.Profiles;
using Conquest.Services.Auth;
using Conquest.Services.Reports;
using Conquest.Services.Moderation;
using Conquest.Services.Blocks;
using Conquest.Services.Redis;
using Conquest.Services.Google;
using Conquest.Services.Recommendations;
using Conquest.Services.Storage;
using Conquest.Services.Notifications;
using Conquest.Services;
using Conquest.Services.Analytics;
using Microsoft.SemanticKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using Serilog;
using Serilog.Events;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;

// Load environment variables from .env file
DotNetEnv.Env.Load();

// --- Serilog Configuration ---
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore.Mvc", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}      {Message:lj}{NewLine}{Exception}")
    // Filter out HostAbortedException (expected during EF migrations - not a real error)
    .Filter.ByExcluding(logEvent => 
        logEvent.Exception is Microsoft.Extensions.Hosting.HostAbortedException)
    .CreateLogger();

try
{
    Log.Information("Starting Conquest API server");

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog();

// Override configuration with environment variables
// ConnectionStrings
var authConnection = Environment.GetEnvironmentVariable("AUTH_CONNECTION");
if (!string.IsNullOrEmpty(authConnection))
    builder.Configuration["ConnectionStrings:AuthConnection"] = authConnection;

var appConnection = Environment.GetEnvironmentVariable("APP_CONNECTION");
if (!string.IsNullOrEmpty(appConnection))
    builder.Configuration["ConnectionStrings:AppConnection"] = appConnection;

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION");
if (!string.IsNullOrEmpty(redisConnection))
    builder.Configuration["ConnectionStrings:RedisConnection"] = redisConnection;

// JWT Settings
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
if (!string.IsNullOrEmpty(jwtKey))
    builder.Configuration["Jwt:Key"] = jwtKey;

var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
if (!string.IsNullOrEmpty(jwtIssuer))
    builder.Configuration["Jwt:Issuer"] = jwtIssuer;

var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
if (!string.IsNullOrEmpty(jwtAudience))
    builder.Configuration["Jwt:Audience"] = jwtAudience;

var jwtAccessTokenMinutes = Environment.GetEnvironmentVariable("JWT_ACCESS_TOKEN_MINUTES");
if (!string.IsNullOrEmpty(jwtAccessTokenMinutes))
    builder.Configuration["Jwt:AccessTokenMinutes"] = jwtAccessTokenMinutes;

// Google API Key
var googleApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
if (!string.IsNullOrEmpty(googleApiKey))
    builder.Configuration["Google:ApiKey"] = googleApiKey;

// Rate Limiting
var rateLimitGlobal = Environment.GetEnvironmentVariable("RATE_LIMIT_GLOBAL_PER_MINUTE");
if (!string.IsNullOrEmpty(rateLimitGlobal))
    builder.Configuration["RateLimiting:GlobalLimitPerMinute"] = rateLimitGlobal;

var rateLimitAuth = Environment.GetEnvironmentVariable("RATE_LIMIT_AUTHENTICATED_PER_MINUTE");
if (!string.IsNullOrEmpty(rateLimitAuth))
    builder.Configuration["RateLimiting:AuthenticatedLimitPerMinute"] = rateLimitAuth;

var rateLimitAuthEndpoints = Environment.GetEnvironmentVariable("RATE_LIMIT_AUTH_ENDPOINTS_PER_MINUTE");
if (!string.IsNullOrEmpty(rateLimitAuthEndpoints))
    builder.Configuration["RateLimiting:AuthEndpointsLimitPerMinute"] = rateLimitAuthEndpoints;

var rateLimitPlaceCreation = Environment.GetEnvironmentVariable("RATE_LIMIT_PLACE_CREATION_PER_DAY");
if (!string.IsNullOrEmpty(rateLimitPlaceCreation))
    builder.Configuration["RateLimiting:PlaceCreationLimitPerDay"] = rateLimitPlaceCreation;




// --- EF Core (SQLite) ---
// Skip SQLite registration when in Testing environment (WebApplicationFactory will use InMemory)
if (builder.Environment.EnvironmentName != "Testing")
{
    // Auth (Identity tables)
    builder.Services.AddDbContext<AuthDbContext>(opt =>
        opt.UseSqlite(builder.Configuration.GetConnectionString("AuthConnection")));

    // App (Places, Activities, etc.)
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlite(builder.Configuration.GetConnectionString("AppConnection"), x => x.UseNetTopologySuite()));
}

// --- Identity ---
builder.Services.AddIdentityCore<AppUser>(opt =>
{
    opt.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AuthDbContext>()
.AddSignInManager<SignInManager<AppUser>>()
.AddDefaultTokenProviders();

// --- JwtOptions bound from config ---
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

JwtOptions jwt;
if (builder.Environment.EnvironmentName == "Testing")
{
    // Provide default JWT settings for testing
    jwt = new JwtOptions 
    { 
        Key = "ThisIsATestSecretKeyForJWTMustBe32CharsLong!",
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        AccessTokenMinutes = 60
    };
}
else
{
    jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Missing Jwt config.");
}

// --- Token service ---
builder.Services.AddScoped<ITokenService, TokenService>();

// --- Redis ---
// Skip Redis in Testing environment (mocked by IntegrationTestFactory)
if (builder.Environment.EnvironmentName != "Testing")
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("RedisConnection");
        options.InstanceName = "Conquest:";
    });

    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var config = builder.Configuration.GetConnectionString("RedisConnection")
            ?? throw new InvalidOperationException("Missing RedisConnection in configuration.");
        return ConnectionMultiplexer.Connect(config);
    });

    builder.Services.AddScoped<IRedisService, RedisService>();
}
else
{
    // In Testing environment, use in-memory cache
    builder.Services.AddDistributedMemoryCache();
}

// --- Session with Redis ---
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// --- Services ---
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddScoped<IPlaceService, PlaceService>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPlaceNameService, GooglePlacesService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IBlockService, BlockService>();
builder.Services.AddScoped<IBusinessService, BusinessService>();
builder.Services.AddScoped<IBusinessAnalyticsService, BusinessAnalyticsService>();
builder.Services.AddScoped<IBanningService, BanningService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHostedService<AnalyticsBackgroundJob>();

// --- AWS S3 & Storage & Email ---
var awsOptions = builder.Configuration.GetAWSOptions();
var awsAccessKey = builder.Configuration["AWS:AccessKey"];
var awsSecretKey = builder.Configuration["AWS:SecretKey"];
if (!string.IsNullOrEmpty(awsAccessKey) && !string.IsNullOrEmpty(awsSecretKey))
{
    awsOptions.Credentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretKey);
}
builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<Amazon.SimpleEmail.IAmazonSimpleEmailService>(); // Add SES
builder.Services.AddScoped<IStorageService, S3StorageService>();
builder.Services.AddScoped<Conquest.Services.Email.IEmailService, Conquest.Services.Email.SesEmailService>(); // Add EmailService
builder.Services.AddHttpClient<Conquest.Services.Moderation.IModerationService, Conquest.Services.Moderation.OpenAIModerationService>();
builder.Services.AddScoped<Conquest.Services.AI.ISemanticService, Conquest.Services.AI.OpenAISemanticService>();
builder.Services.AddScoped<RecommendationService>();


// --- Health Checks ---
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AuthDbContext>()
    .AddDbContextCheck<AppDbContext>()
    .ForwardToPrometheus();

// --- Semantic Kernel ---
builder.Services.AddKernel(); // Always register Kernel

var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (!string.IsNullOrEmpty(openAiKey))
{
    builder.Services.AddOpenAIChatCompletion("gpt-3.5-turbo", openAiKey);
}
else
{
    // Fallback if no key provided (prevents crash, but AI features won't work)
    // In production you might want to throw or log a warning
    Console.WriteLine("WARNING: OPENAI_API_KEY is missing. AI features will be disabled.");
}

// --- JWT Auth ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
    });

builder.Services.AddAuthorization();

// --- Controllers + Swagger ---
builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"),
            new QueryStringApiVersionReader("api-version"));
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddTransient<Conquest.Middleware.GlobalExceptionHandler>();
builder.Services.AddScoped<Conquest.Middleware.RateLimitMiddleware>();
builder.Services.AddScoped<Conquest.Middleware.BanningMiddleware>();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Conquest API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter: Bearer {token}"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference
            { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

var app = builder.Build();

// --- Seed Roles ---
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roleNames = { "Admin", "User", "Business" };
    foreach (var roleName in roleNames)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }
}


// --- Middleware ---
app.UseMiddleware<Conquest.Middleware.GlobalExceptionHandler>();

// Serilog request logging (logs all HTTP requests with timing)
app.UseSerilogRequestLogging(options =>
{
    // Customize the message template
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    
    // Attach additional properties to the request completion event
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].FirstOrDefault());
    };
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Conquest API v1");
    c.RoutePrefix = string.Empty; // Swagger UI at "/"
});

// Log Redis connection status
using (var scope = app.Services.CreateScope())
{
    var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    if (redis.IsConnected)
    {
        logger.LogInformation("✓ Redis connected successfully");
    }
    else
    {
        logger.LogError("✗ Redis connection failed - rate limiting and caching will not work");
    }
}

app.UseMiddleware<Conquest.Middleware.ResponseMetricMiddleware>();
app.UseRouting();
app.UseHttpMetrics();
app.UseSession();
app.UseAuthentication();
app.UseMiddleware<Conquest.Middleware.BanningMiddleware>();
app.UseMiddleware<Conquest.Middleware.RateLimitMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapMetrics();
app.MapHealthChecks("/health");

// Capture JVM-style runtime metrics (GC, ThreadPool, etc.)
DotNetRuntimeStatsBuilder.Default().StartCollecting();

app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
