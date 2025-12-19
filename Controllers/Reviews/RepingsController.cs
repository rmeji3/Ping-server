using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Ping.Dtos.Reviews;
using Ping.Services.Reviews;
using Ping.Dtos.Common;
using Ping.Models.AppUsers;

namespace Ping.Controllers.Reviews;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
public class RepingsController(IRepingService repingService) : ControllerBase
{
    // POST /api/repings/review/{reviewId}
    [HttpPost("review/{reviewId:int}")]
    public async Task<ActionResult<RepingDto>> RepostReview(int reviewId, [FromBody] RepostReviewDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        try
        {
            var result = await repingService.RepostReviewAsync(reviewId, userId, dto);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // DELETE /api/repings/review/{reviewId}
    [HttpDelete("review/{reviewId:int}")]
    public async Task<IActionResult> DeleteReping(int reviewId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        try
        {
            await repingService.DeleteRepingAsync(reviewId, userId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // GET /api/repings/user/{userId}
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<PaginatedResult<RepingDto>>> GetUserRepings(string userId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };

        try
        {
            var result = await repingService.GetUserRepingsAsync(userId, currentUserId!, pagination);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // PATCH /api/repings/review/{reviewId}/privacy
    [HttpPatch("review/{reviewId:int}/privacy")]
    public async Task<IActionResult> UpdatePrivacy(int reviewId, [FromBody] RepostReviewDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        try
        {
            await repingService.UpdateRepingPrivacyAsync(reviewId, userId, dto.Privacy);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
