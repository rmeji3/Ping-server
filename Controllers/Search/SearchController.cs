using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Ping.Dtos.Search;
using Ping.Services.Search;

namespace Ping.Controllers.Search;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class SearchController(ISearchService searchService) : ControllerBase
{
    /// <summary>
    /// Unified search across Profiles, Pings, and Events.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<UnifiedSearchResultDto>> UnifiedSearch([FromQuery] UnifiedSearchFilterDto filter)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        try
        {
            var results = await searchService.UnifiedSearchAsync(filter, userId);
            return Ok(results);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Get recent search history for the current user.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<SearchHistoryDto>>> GetHistory([FromQuery] int count = 20)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var history = await searchService.GetSearchHistoryAsync(userId, count);
        return Ok(history);
    }

    /// <summary>
    /// Add an item to search history.
    /// </summary>
    [HttpPost("history")]
    public async Task<IActionResult> AddToHistory([FromBody] CreateSearchHistoryDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await searchService.AddToHistoryAsync(userId, input);
        return Ok();
    }

    /// <summary>
    /// Delete a specific history item.
    /// </summary>
    [HttpDelete("history/{id}")]
    public async Task<IActionResult> DeleteHistoryItem(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await searchService.DeleteHistoryItemAsync(userId, id);
        return NoContent();
    }

    /// <summary>
    /// Clear all search history for the current user.
    /// </summary>
    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await searchService.ClearHistoryAsync(userId);
        return NoContent();
    }
}
