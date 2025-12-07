using System.Security.Claims;
using Conquest.Dtos.Reviews;
using Conquest.Services.Reviews;
using Microsoft.AspNetCore.Mvc;

namespace Conquest.Controllers.Reviews;

[ApiController]
[Route("api/activities/{placeActivityId:int}/[controller]")]
public class ReviewsController(IReviewService reviewService, ILogger<ReviewsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ReviewDto>> CreateReview(int placeActivityId, [FromBody] CreateReviewDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.Identity?.Name;
        
        if (userId is null || userName is null)
        {
            logger.LogWarning("CreateReview: User is not authenticated or missing id/username.");
            return Unauthorized("User is not authenticated or missing id/username.");
        }

        try
        {
            var result = await reviewService.CreateReviewAsync(placeActivityId, dto, userId, userName);
            return CreatedAtAction(nameof(GetReviews), new { placeActivityId, scope = "mine" }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }
    

    // GET /api/activities/{placeActivityId}/reviews?scope=mine|global|friends
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserReviewsDto>>> GetReviews(int placeActivityId,
        [FromQuery] string scope = "global")
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            logger.LogWarning("GetReviews: User is not authenticated or missing id.");
            return Unauthorized();
        }
        
        try
        {
            var result = await reviewService.GetReviewsAsync(placeActivityId, scope, userId);
            logger.LogInformation("GetReviews: Reviews fetched for Activity {PlaceActivityId} by {UserName}. Scope: {Scope}", placeActivityId, userId, scope);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning("GetReviews: Activity {PlaceActivityId} not found.", placeActivityId);
            return NotFound(ex.Message);
        }
    }

    // GET /api/reviews/explore
    [HttpGet("/api/reviews/explore")]
    public async Task<ActionResult<IEnumerable<ExploreReviewDto>>> GetExploreReviews(
        [FromQuery] ExploreReviewsFilterDto filter)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviews = await reviewService.GetExploreReviewsAsync(filter, userId);
        return Ok(reviews);
    }

    // POST /api/reviews/{reviewId}/like
    [HttpPost("{reviewId:int}/like")]
    public async Task<IActionResult> LikeReview(int reviewId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            logger.LogWarning("LikeReview: User is not authenticated or missing id.");
            return Unauthorized();
        }
        
        try
        {
            await reviewService.LikeReviewAsync(reviewId, userId);
            logger.LogInformation("LikeReview: Review {ReviewId} liked by {UserName}", reviewId, userId);
            return Ok();
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning("LikeReview: Review {ReviewId} not found.", reviewId);
            return NotFound(ex.Message);
        }
    }

    // POST /api/reviews/{reviewId}/unlike
    [HttpPost("{reviewId:int}/unlike")]
    public async Task<IActionResult> UnlikeReview(int reviewId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            logger.LogWarning("UnlikeReview: User is not authenticated or missing id.");
            return Unauthorized();
        }
        
        try
        {
            await reviewService.UnlikeReviewAsync(reviewId, userId);
            logger.LogInformation("UnlikeReview: Review {ReviewId} unliked by {UserName}", reviewId, userId);
            return Ok();
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning("UnlikeReview: Review {ReviewId} not found.", reviewId);
            return NotFound(ex.Message);
        }
    }

    // GET /api/reviews/liked
    [HttpGet("/api/reviews/liked")]
    public async Task<ActionResult<IEnumerable<ExploreReviewDto>>> GetLikedReviews()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            logger.LogWarning("GetLikedReviews: User is not authenticated or missing id.");
            return Unauthorized();
        }

        var reviews = await reviewService.GetLikedReviewsAsync(userId);
        logger.LogInformation("GetLikedReviews: Liked reviews fetched for {UserId}", userId);
        return Ok(reviews);
    }

    // GET /api/reviews/me
    [HttpGet("/api/reviews/me")]
    public async Task<ActionResult<IEnumerable<ExploreReviewDto>>> GetMyReviews()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            logger.LogWarning("GetMyReviews: User is not authenticated or missing id.");
            return Unauthorized();
        }

        var reviews = await reviewService.GetMyReviewsAsync(userId);
        logger.LogInformation("GetMyReviews: Reviews fetched for {UserId}", userId);
        return Ok(reviews);
    }
}
