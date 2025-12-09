using System.Text;
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
using Conquest.Services.Tags;
using Conquest.Services.Activities;
using Conquest.Services.Profiles;
using Conquest.Services.Auth;
using Conquest.Services.Redis;
using Conquest.Services.Google;
using Conquest.Services.Recommendations;
using Conquest.Services.Storage;
using Microsoft.SemanticKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

// Load environment variables from .env file
DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

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
// change to postgres later for production
// Auth (Identity tables)
builder.Services.AddDbContext<AuthDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("AuthConnection")));

// App (Places, Activities, etc.)
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("AppConnection")));

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
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Missing Jwt config.");

// --- Token service ---
builder.Services.AddScoped<ITokenService, TokenService>();

// --- Redis ---
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

// --- AWS S3 & Storage ---
var awsOptions = builder.Configuration.GetAWSOptions();
// Explicitly set credentials if they are in the config (e.g. UserSecrets)
// This fixes issues where the SDK fails to resolve them from the "AWS" section automatically
var awsAccessKey = builder.Configuration["AWS:AccessKey"];
var awsSecretKey = builder.Configuration["AWS:SecretKey"];
if (!string.IsNullOrEmpty(awsAccessKey) && !string.IsNullOrEmpty(awsSecretKey))
{
    awsOptions.Credentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretKey);
}
builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<IStorageService, S3StorageService>();
builder.Services.AddHttpClient<Conquest.Services.Moderation.IModerationService, Conquest.Services.Moderation.OpenAIModerationService>();
builder.Services.AddScoped<Conquest.Services.AI.ISemanticService, Conquest.Services.AI.OpenAISemanticService>();
builder.Services.AddScoped<RecommendationService>();

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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddTransient<Conquest.Middleware.GlobalExceptionHandler>();
builder.Services.AddScoped<Conquest.Middleware.RateLimitMiddleware>();
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

// --- Auto-migrate DB on startup (optional) ---
using (var scope = app.Services.CreateScope())
{
    var authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    authDb.Database.Migrate();

    var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    appDb.Database.Migrate();

    // --- Seed Roles ---
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roleNames = { "Admin", "User" };
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

app.UseRouting();
app.UseMiddleware<Conquest.Middleware.RateLimitMiddleware>();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
