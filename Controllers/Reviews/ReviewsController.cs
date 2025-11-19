using System.Security.Claims;
using Conquest.Data.App;
using Conquest.Dtos.Reviews;
using Conquest.Models.Reviews;
using Conquest.Services.Friends;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Conquest.Controllers.Reviews;

[ApiController]
[Route("api/activities/{placeActivityId:int}/[controller]")]
public class ReviewsController(AppDbContext appDb, IFriendService friendService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ReviewDto>> CreateReview(int placeActivityId, [FromBody] CreateReviewDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.Identity?.Name;
        
        if (userId is null || userName is null)
        {
            return Unauthorized("User is not authenticated or missing id/username.");
        }

        // Ensure activity exists
        var activityExists = await appDb.PlaceActivities
            .AnyAsync(pa => pa.Id == placeActivityId);

        if (!activityExists)
            return NotFound("Activity not found.");

        var review = new Review
        {
            PlaceActivityId = placeActivityId,
            UserId = userId,
            UserName = userName,
            Rating = dto.Rating,
            Content = dto.Content,
            CreatedAt = DateTime.UtcNow,
        };

        appDb.Reviews.Add(review);
        await appDb.SaveChangesAsync();

        var result = new ReviewDto(
            review.Id,
            review.Rating,
            review.Content,
            review.UserName,
            review.CreatedAt
        );
        
        return CreatedAtAction(nameof(GetReviews), new { placeActivityId, scope = "mine" }, result);
    }
    

    // GET /api/activities/{placeActivityId}/reviews?scope=mine|global|friends
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReviewDto>>> GetReviews(int placeActivityId,
        [FromQuery] string scope = "global")
    {
        var query = appDb.Reviews
            .Where(r => r.PlaceActivityId == placeActivityId)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }
        
        var activityExists = await appDb.PlaceActivities
            .AnyAsync(pa => pa.Id == placeActivityId);
        if (!activityExists)
            return NotFound("Activity not found.");

        
        switch (scope.ToLowerInvariant())
        {
            case "mine":
                {
                    query = query.Where(r => r.UserId == userId);
                    break;
                }
            case "friends":
                {
                    var friendIds = await friendService.GetFriendIdsAsync(userId);
                    if (friendIds.Count == 0)
                    {
                        // no friends → no reviews in this scope
                        return Ok(Array.Empty<ReviewDto>());
                    }
                    query = query.Where(r => friendIds.Contains(r.UserId));
                    break;
                }
            case "global":
            default:
                // no extra filter
                break;
        }
        var reviews = await query
            .Select(r => new ReviewDto(
                r.Id,
                r.Rating,
                r.Content,
                r.UserName,
                r.CreatedAt
            ))
            .ToListAsync();

        return Ok(reviews);
    }
}
