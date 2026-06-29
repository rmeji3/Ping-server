using Xunit;
using Xunit.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Moq;

namespace Ping.Tests.Controllers;

public class GooglePlacesControllerTests : BaseIntegrationTest
{
    private readonly ITestOutputHelper _output;

    public GooglePlacesControllerTests(IntegrationTestFactory factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _sendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task Autocomplete_WithCitiesType_ShouldRespectTypesParameterAndNotFilterCities()
    {
        // Arrange
        string? requestedUrl = null;
        
        var mockHandler = new MockHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestedUrl = request.RequestUri?.ToString();
            
            // Return mock response representing cities
            var responsePayload = new
            {
                status = "OK",
                predictions = new[]
                {
                    new
                    {
                        description = "Chicago, IL, USA",
                        place_id = "chicago_id",
                        types = new[] { "locality", "political", "geocode" }
                    }
                }
            };
            
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload), System.Text.Encoding.UTF8, "application/json")
            };
            return await Task.FromResult(response);
        });

        var mockClientFactory = new Mock<IHttpClientFactory>();
        mockClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler));

        // Create a test client with overridden config and mocked factory
        var testFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Google:ApiKey"] = "my-test-google-api-key"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(mockClientFactory.Object);
            });
        });

        var testClient = testFactory.CreateClient();

        // Act
        var response = await testClient.GetAsync("/api/googleplaces/autocomplete/json?input=Chicago&types=(cities)");
        var content = await response.Content.ReadAsStringAsync();
        
        _output.WriteLine($"Requested URL: {requestedUrl}");
        _output.WriteLine($"Response JSON: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(requestedUrl);
        Assert.Contains("types=%28cities%29", requestedUrl); // URL encoded '(cities)' is '%28cities%29'
        
        // Assert that the cities are NOT filtered out by the FilterEstablishmentsOnly post-filter
        Assert.Contains("Chicago, IL, USA", content);
    }

    [Fact]
    public async Task Autocomplete_WithEstablishmentType_ShouldRespectTypesParameterAndFilterGeocodes()
    {
        // Arrange
        string? requestedUrl = null;
        
        var mockHandler = new MockHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestedUrl = request.RequestUri?.ToString();
            
            // Return mock response representing cities + establishments
            var responsePayload = new
            {
                status = "OK",
                predictions = new[]
                {
                    new
                    {
                        description = "Starbucks Chicago, IL, USA",
                        place_id = "starbucks_id",
                        types = new[] { "establishment", "food", "point_of_interest" }
                    },
                    new
                    {
                        description = "Chicago, IL, USA",
                        place_id = "chicago_id",
                        types = new[] { "locality", "political", "geocode" }
                    }
                }
            };
            
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload), System.Text.Encoding.UTF8, "application/json")
            };
            return await Task.FromResult(response);
        });

        var mockClientFactory = new Mock<IHttpClientFactory>();
        mockClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(mockHandler));

        // Create a test client with overridden config and mocked factory
        var testFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Google:ApiKey"] = "my-test-google-api-key"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(mockClientFactory.Object);
            });
        });

        var testClient = testFactory.CreateClient();

        // Act
        var response = await testClient.GetAsync("/api/googleplaces/autocomplete/json?input=Starbucks&types=establishment");
        var content = await response.Content.ReadAsStringAsync();
        
        _output.WriteLine($"Requested URL: {requestedUrl}");
        _output.WriteLine($"Response JSON: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(requestedUrl);
        Assert.Contains("types=establishment", requestedUrl);
        
        // Assert that the city was filtered out by the FilterEstablishmentsOnly post-filter, but Starbucks remained
        Assert.Contains("Starbucks Chicago, IL, USA", content);
        Assert.DoesNotContain("chicago_id", content); // Chicago locality was filtered out because we passed types=establishment
    }
}
