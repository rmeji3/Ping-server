using Xunit;
using Xunit.Abstractions;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http.Json;
using Ping.Dtos.Pings;
using Ping.Dtos.Activities;
using Ping.Models.Pings;
using Ping.Data.Auth;
using Ping.Data.App;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Ping.Tests.Controllers.Activities;

public class RateLimitActivityTests : BaseIntegrationTest
{
    private readonly ITestOutputHelper _output;

    public RateLimitActivityTests(IntegrationTestFactory factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    [Fact]
    public async Task CreateActivity_RateLimit_ShouldReturn400()
    {
        // Arrange
        int callCount = 0;
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Ping.Services.Redis.IRedisService>();
                var mockRedis = new Mock<Ping.Services.Redis.IRedisService>();
                
                // Only increment for activity rate limit keys
                mockRedis.Setup(x => x.IncrementAsync(It.Is<string>(s => s.StartsWith("ratelimit:activity:")), It.IsAny<TimeSpan?>()))
                    .ReturnsAsync(() => ++callCount);
                
                // Other keys (like ping creation) should not increment the same counter
                mockRedis.Setup(x => x.IncrementAsync(It.Is<string>(s => !s.StartsWith("ratelimit:activity:")), It.IsAny<TimeSpan?>()))
                    .ReturnsAsync(1);
                    
                services.AddScoped(_ => mockRedis.Object);
            });
        }).CreateClient();

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        // Authenticate the customized client
        Authenticate("limituser");
        client.DefaultRequestHeaders.Authorization = _client.DefaultRequestHeaders.Authorization;

        // Ensure databases are created for this specific client's context if needed
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        }

        // 1. Create a place
        var placeRequest = new UpsertPingDto(
            "Rate Limit Place", 
            "123 Limit St", 
            40.7128, 
            -74.0060, 
            PingVisibility.Public, 
            PingType.Verified,
            null, null
            );
        
        var placeResponse = await client.PostAsJsonAsync("/api/pings", placeRequest);
        var place = await placeResponse.Content.ReadFromJsonAsync<PingDetailsDto>(options);
        Assert.NotNull(place);

        // 2. Create 10 activities (the limit is 10)
        for (int i = 1; i <= 10; i++)
        {
            var activityRequest = new CreatePingActivityDto(place.Id, $"Activity {i}", null);
            var response = await client.PostAsJsonAsync("/api/ping-activities", activityRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // 3. Create the 11th activity -> Should fail
        var activityRequest11 = new CreatePingActivityDto(place.Id, "Activity 11", null);
        var response11 = await client.PostAsJsonAsync("/api/ping-activities", activityRequest11);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response11.StatusCode);
        var errorBody = await response11.Content.ReadAsStringAsync();
        _output.WriteLine($"Error response: {errorBody}");
        Assert.Contains("Too many activities created", errorBody);
    }
}
