using Xunit;
using Xunit.Abstractions;
using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Ping.Data.App;

namespace Ping.Tests.Controllers;

public class ReviewsControllerTests : BaseIntegrationTest
{
    private readonly ITestOutputHelper _output;

    public ReviewsControllerTests(IntegrationTestFactory factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    [Fact]
    public async Task GetExploreReviews_WithRadiusKmButNoCoordinates_ShouldNotFail()
    {
        // Arrange
        var userId = Authenticate("user_explore");

        // Act
        var response = await Client.GetAsync("/api/reviews/explore?radiusKm=160.93&scope=global");
        
        var content = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Status: {response.StatusCode}");
        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetExploreReviews_WithCoordinates_ShouldNotThrow500()
    {
        // Arrange
        var userId = Authenticate("user_explore_2");

        // Act
        var response = await Client.GetAsync("/api/reviews/explore?latitude=41.8781&longitude=-87.6298&radiusKm=160.93&scope=global");
        
        var content = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Status: {response.StatusCode}");
        _output.WriteLine($"Response: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
