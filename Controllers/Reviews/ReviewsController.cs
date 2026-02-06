using System.Security.Claims;
using Ping.Dtos.Reviews;
using Ping.Services.Reviews;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Ping.Dtos.Common;

namespace Ping.Controllers.Reviews
{

    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/ping-activities/{pingActivityId:int}/[controller]")]
    [Route("api/v{version:apiVersion}/ping-activities/{pingActivityId:int}/[controller]")]
    public class ReviewsController(IReviewService reviewService, Ping.Services.Images.IImageService imageService, ILogger<ReviewsController> logger) : ControllerBase
    {
        public class CreateReviewRequest
        {
            public int Rating { get; set; }
            public string? Content { get; set; }
            public IFormFile? Image { get; set; }
            public List<string>? Tags { get; set; }
        }

        public class UpdateReviewRequest
        {
            public int? Rating { get; set; }
            public string? Content { get; set; }
            public IFormFile? Image { get; set; }
            public List<string>? Tags { get; set; }
            
            // Allow manual URL overrides if needed, though Image file takes precedence
            public string? ImageUrl { get; set; }
            public string? ThumbnailUrl { get; set; }
        }

        [HttpPost]
        public async Task<ActionResult<ReviewDto>> CreateReview(int pingActivityId, [FromForm] CreateReviewRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = User.Identity?.Name;
            
            if (userId is null || userName is null)
            {
                logger.LogWarning("CreateReview: User is not authenticated or missing id/username.");
                return Unauthorized("User is not authenticated or missing id/username.");
            }

            string imageUrl = "";
            string thumbnailUrl = "";

            // Handle Image Upload
            if (request.Image != null)
            {
                try 
                {
                    // "reviews" acts as the folder
                    var (original, thumb) = await imageService.ProcessAndUploadImageAsync(request.Image, "reviews", userId);
                    imageUrl = original;
                    thumbnailUrl = thumb;
                }
                catch (Exception ex)
                {
                     logger.LogError(ex, "Failed to upload image for review.");
                     return BadRequest("Failed to process image.");
                }
            }

            // Map to DTO
            var dto = new CreateReviewDto(
                request.Rating,
                request.Content,
                imageUrl,
                thumbnailUrl,
                request.Tags
            );

            try
            {
                var result = await reviewService.CreateReviewAsync(pingActivityId, dto, userId, userName);
                return CreatedAtAction(nameof(GetReviews), new { pingActivityId, scope = "mine" }, result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
        

        // GET /api/ping-activities/{pingActivityId}/reviews?scope=mine|global|friends&pageNumber=1&pageSize=20
        [HttpGet]
        public async Task<ActionResult<PaginatedResult<ReviewDto>>> GetReviews(int pingActivityId,
            [FromQuery] string scope = "global",
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                logger.LogWarning("GetReviews: User is not authenticated or missing id.");
                return Unauthorized();
            }
            
            try
            {
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var result = await reviewService.GetReviewsAsync(pingActivityId, scope, userId, pagination);
                logger.LogInformation("GetReviews: Reviews fetched for Activity {PingActivityId} by {UserName}. Scope: {Scope}", pingActivityId, userId, scope);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                logger.LogWarning("GetReviews: Activity {PingActivityId} not found.", pingActivityId);
                return NotFound(ex.Message);
            }
        }

        // GET /api/reviews/explore
        [HttpGet("/api/reviews/explore")]
        [HttpGet("/api/v{version:apiVersion}/reviews/explore")]
        public async Task<ActionResult<PaginatedResult<ExploreReviewDto>>> GetExploreReviews(
            [FromQuery] ExploreReviewsFilterDto filter,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var reviews = await reviewService.GetExploreReviewsAsync(filter, userId, pagination);
            return Ok(reviews);
        }

        // POST /api/reviews/{reviewId}/like
        [HttpPost("/api/reviews/{reviewId:int}/like")]
        [HttpPost("/api/v{version:apiVersion}/reviews/{reviewId:int}/like")]
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

        // DELETE /api/reviews/{reviewId}/like
        [HttpDelete("/api/reviews/{reviewId:int}/like")]
        [HttpDelete("/api/v{version:apiVersion}/reviews/{reviewId:int}/like")]
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
        [HttpGet("/api/v{version:apiVersion}/reviews/liked")]
        public async Task<ActionResult<PaginatedResult<ExploreReviewDto>>> GetLikedReviews(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                logger.LogWarning("GetLikedReviews: User is not authenticated or missing id.");
                return Unauthorized();
            }

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var reviews = await reviewService.GetLikedReviewsAsync(userId, pagination);
            logger.LogInformation("GetLikedReviews: Liked reviews fetched for {UserId}", userId);
            return Ok(reviews);
        }

        // GET /api/reviews/me
        [HttpGet("/api/reviews/me")]
        [HttpGet("/api/v{version:apiVersion}/reviews/me")]
        public async Task<ActionResult<PaginatedResult<ExploreReviewDto>>> GetMyReviews(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                logger.LogWarning("GetMyReviews: User is not authenticated or missing id.");
                return Unauthorized();
            }

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var reviews = await reviewService.GetMyReviewsAsync(userId, pagination);
            logger.LogInformation("GetMyReviews: Reviews fetched for {UserId}", userId);
            return Ok(reviews);
        }
        // GET /api/reviews/friends
        [HttpGet("/api/reviews/friends")]
        [HttpGet("/api/v{version:apiVersion}/reviews/friends")]
        public async Task<ActionResult<PaginatedResult<ExploreReviewDto>>> GetFriendsFeed(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                logger.LogWarning("GetFriendsFeed: User is not authenticated or missing id.");
                return Unauthorized();
            }

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var reviews = await reviewService.GetFriendsFeedAsync(userId, pagination);
            logger.LogInformation("GetFriendsFeed: Friends feed fetched for {UserId}", userId);
            return Ok(reviews);
        }

        // DELETE /api/reviews/{reviewId}
        [HttpDelete("/api/reviews/{reviewId:int}")]
        [HttpDelete("/api/v{version:apiVersion}/reviews/{reviewId:int}")]
        public async Task<IActionResult> DeleteReview(int reviewId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                logger.LogWarning("DeleteReview: User is not authenticated or missing id.");
                return Unauthorized();
            }

            try
            {
                await reviewService.DeleteReviewAsync(reviewId, userId);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
        }

        // PATCH /api/reviews/{reviewId}
        [HttpPatch("/api/reviews/{reviewId:int}")]
        [HttpPatch("/api/v{version:apiVersion}/reviews/{reviewId:int}")]
        public async Task<ActionResult<ReviewDto>> UpdateReview(int reviewId, [FromForm] UpdateReviewRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                logger.LogWarning("UpdateReview: User is not authenticated or missing id.");
                return Unauthorized();
            }

            string? imgUrl = request.ImageUrl;
            string? thumbUrl = request.ThumbnailUrl;

            // Handle Image Upload
            if (request.Image != null)
            {
                try 
                {
                    var (original, thumb) = await imageService.ProcessAndUploadImageAsync(request.Image, "reviews", userId);
                    imgUrl = original;
                    thumbUrl = thumb;
                }
                catch (Exception ex)
                {
                     logger.LogError(ex, "Failed to upload image for review update.");
                     return BadRequest("Failed to process image.");
                }
            }

            // Map to DTO
            var dto = new UpdateReviewDto(
                request.Rating,
                request.Content,
                imgUrl,
                thumbUrl,
                request.Tags
            );

            try
            {
                var result = await reviewService.UpdateReviewAsync(reviewId, userId, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
