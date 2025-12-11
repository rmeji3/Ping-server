using Conquest.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Conquest.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/admin/analytics")]
[Route("api/v{version:apiVersion}/admin/analytics")]
[Authorize(Roles = "Admin")]
public class AdminAnalyticsController(IAnalyticsService analytics) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardStats()
    {
        var stats = await analytics.GetDashboardStatsAsync();
        return Ok(stats);
    }

    [HttpGet("trending")]
    public async Task<IActionResult> GetTrendingPlaces()
    {
        var places = await analytics.GetTrendingPlacesAsync();
        return Ok(places);
    }

    [HttpGet("moderation")]
    public async Task<IActionResult> GetModerationStats()
    {
        var stats = await analytics.GetModerationStatsAsync();
        return Ok(stats);
    }

    [HttpGet("growth")]
    public async Task<IActionResult> GetGrowth([FromQuery] string type = "DAU", [FromQuery] int days = 30)
    {
        var data = await analytics.GetHistoricalGrowthAsync(type, days);
        return Ok(data);
    }

    [HttpGet("growth/region")]
    public async Task<IActionResult> GetGrowthByRegion()
    {
        var data = await analytics.GetGrowthByRegionAsync();
        return Ok(data);
    }

    [HttpPost("compute-now")]
    public async Task<IActionResult> ManualCompute()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await analytics.ComputeDailyMetricsAsync(today);
        return Ok("Metrics computed for today.");
    }
}
