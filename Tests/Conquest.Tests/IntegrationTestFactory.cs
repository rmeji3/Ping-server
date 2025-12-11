using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Conquest.Data.Auth;
using Conquest.Data.App;
using StackExchange.Redis;
using Moq;

namespace Conquest.Tests;

public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public string AuthDbName { get; } = "TestAuthDb_" + Guid.NewGuid();
    public string AppDbName { get; } = "TestAppDb_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        // Add test configuration for JWT
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "ThisIsATestSecretKeyForJWTMustBe32CharsLong!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:AccessTokenMinutes"] = "60"
            });
        });
        
        builder.ConfigureServices(services =>
        {
            // Remove the original DbContext registrations
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<AppDbContext>();
            
            // Add InMemory DbContexts
            services.AddDbContext<AuthDbContext>(options =>
            {
                options.UseInMemoryDatabase(AuthDbName);
            });

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(AppDbName);
            });

            // Replace Redis with Mock (if not already skipped by Program.cs)
            services.RemoveAll<IConnectionMultiplexer>();
            var mockMultiplexer = new Moq.Mock<IConnectionMultiplexer>();
            mockMultiplexer.Setup(m => m.IsConnected).Returns(true);
            services.AddSingleton(mockMultiplexer.Object);

            // Replace IDistributedCache with in-memory version
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            // Replace IRedisService with Mock
            services.RemoveAll<Conquest.Services.Redis.IRedisService>();
            var mockRedis = new Moq.Mock<Conquest.Services.Redis.IRedisService>();
            mockRedis.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>())).ReturnsAsync(1);
            services.AddScoped(_ => mockRedis.Object);

            // Replace ISemanticService with Mock
            services.RemoveAll<Conquest.Services.AI.ISemanticService>();
            var mockSemantic = new Moq.Mock<Conquest.Services.AI.ISemanticService>();
            mockSemantic.Setup(x => x.FindDuplicateAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>())).ReturnsAsync((string?)null);
            services.AddScoped(_ => mockSemantic.Object);

            // Replace IModerationService with Mock
            services.RemoveAll<Conquest.Services.Moderation.IModerationService>();
            var mockModeration = new Moq.Mock<Conquest.Services.Moderation.IModerationService>();
            mockModeration.Setup(x => x.CheckContentAsync(It.IsAny<string>())).ReturnsAsync(new Conquest.Services.Moderation.ModerationResult(false, ""));
            services.AddScoped(_ => mockModeration.Object);

            // Add Mock IChatCompletionService
            services.AddSingleton(new Moq.Mock<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>().Object);

            // Mock GoogleAuthService
            services.RemoveAll<Conquest.Services.Google.GoogleAuthService>();
            // GoogleAuthService(IConfiguration)
            var mockGoogle = new Moq.Mock<Conquest.Services.Google.GoogleAuthService>(
                new Moq.Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object
            );
            services.AddScoped(_ => mockGoogle.Object);

            // Mock AppleAuthService
            services.RemoveAll<Conquest.Services.Apple.AppleAuthService>();
            // AppleAuthService(IHttpClientFactory, IMemoryCache, IConfiguration)
            var mockCache = new Moq.Mock<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var mockClientFactory = new Moq.Mock<IHttpClientFactory>();
            mockClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

            var mockApple = new Moq.Mock<Conquest.Services.Apple.AppleAuthService>(
                mockClientFactory.Object,
                mockCache.Object,
                new Moq.Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object
            );
            services.AddScoped(_ => mockApple.Object);

            // Mock IEmailService
            services.RemoveAll<Conquest.Services.Email.IEmailService>();
            var mockEmail = new Moq.Mock<Conquest.Services.Email.IEmailService>();
            services.AddScoped(_ => mockEmail.Object);

            // Mock IStorageService
            services.RemoveAll<Conquest.Services.Storage.IStorageService>();
            var mockStorage = new Moq.Mock<Conquest.Services.Storage.IStorageService>();
            mockStorage.Setup(x => x.UploadFileAsync(It.IsAny<Microsoft.AspNetCore.Http.IFormFile>(), It.IsAny<string>())).ReturnsAsync("https://mock-s3.com/file.jpg");
            services.AddScoped(_ => mockStorage.Object);
        });
    }
}
