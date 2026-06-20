using System.Security.Claims;
using Ping.Dtos.Reviews;
using Ping.Services.Reviews;
using Microsoft.AspNetCore.Authorization;
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
            public List<IFormFile>? Images { get; set; }
            public List<string>? Tags { get; set; }
        }

        public class UpdateReviewRequest
        {
            public int? Rating { get; set; }
            public string? Content { get; set; }
            public IFormFile? Image { get; set; }
            public List<IFormFile>? Images { get; set; }
            public List<string>? Tags { get; set; }
            
            // Allow manual URL overrides if needed, though Image file takes precedence
            public string? ImageUrl { get; set; }
            public string? ThumbnailUrl { get; set; }

            // When provided (non-null), the client is authoritative over the full, ordered image
            // set. Each entry is either an existing image URL (to keep) or a "new:<n>" token that
            // references the n-th newly uploaded file in Images. The first entry becomes the cover.
            // An empty list clears all images. When null, legacy behavior applies.
            public List<string>? ImageOrder { get; set; }
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
            var additionalImages = new List<string>();

            var allImagesToProcess = new List<IFormFile>();
            if (request.Image != null) allImagesToProcess.Add(request.Image);
            if (request.Images != null) allImagesToProcess.AddRange(request.Images);
            allImagesToProcess = allImagesToProcess.Take(6).ToList();

            // Handle Image Upload
            if (allImagesToProcess.Any())
            {
                try 
                {
                    bool isFirst = true;
                    foreach(var imgFile in allImagesToProcess)
                    {
                        var (original, thumb) = await imageService.ProcessAndUploadImageAsync(imgFile, "reviews", userId);
                        if (isFirst) {
                            imageUrl = original;
                            thumbnailUrl = thumb;
                            isFirst = false;
                        } else {
                            additionalImages.Add(original);
                        }
                    }
                }
                catch (Exception ex)
                {
                     logger.LogError(ex, "Failed to upload image for review.");
                     return BadRequest("Failed to process image.");
                }
            }
            else 
            {
                // If no file, use placeholders or error out if strictly required by business logic.
                // Review model requires ImageUrl, so we use placeholder.
                imageUrl = Ping.Utils.UrlUtils.SanitizeUrl(null);
                thumbnailUrl = imageUrl;
            }

            // Map to DTO
            var dto = new CreateReviewDto(
                request.Rating,
                request.Content,
                imageUrl,
                thumbnailUrl,
                request.Tags,
                additionalImages
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
        // Requires auth: the feed is user-relative (IsOwner, liked state), and the client
        // only calls it while authenticated. Without this, an expired token is silently
        // treated as anonymous (userId=null) -> IsOwner=false on the user's own reviews,
        // and no 401 is returned so the client's refresh-on-401 flow never fires.
        [Authorize]
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
            List<string>? additionalToSave;

            // Upload any new image files first, preserving order (Image, then Images).
            var newImageFiles = new List<IFormFile>();
            if (request.Image != null) newImageFiles.Add(request.Image);
            if (request.Images != null) newImageFiles.AddRange(request.Images);
            newImageFiles = newImageFiles.Take(6).ToList();

            var uploaded = new List<(string original, string thumb)>();
            if (newImageFiles.Any())
            {
                try
                {
                    foreach (var imgFile in newImageFiles)
                    {
                        var (original, thumb) = await imageService.ProcessAndUploadImageAsync(imgFile, "reviews", userId);
                        uploaded.Add((original, thumb));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to upload image for review update.");
                    return BadRequest("Failed to process image.");
                }
            }

            if (request.ImageOrder != null)
            {
                // Client-authoritative: reconstruct the full ordered set from the tokens.
                var final = new List<(string url, string? thumb)>();
                foreach (var token in request.ImageOrder)
                {
                    if (token != null && token.StartsWith("new:"))
                    {
                        if (int.TryParse(token.Substring(4), out var n) && n >= 0 && n < uploaded.Count)
                        {
                            final.Add((uploaded[n].original, uploaded[n].thumb));
                        }
                    }
                    else
                    {
                        var clean = Ping.Utils.UrlUtils.SanitizeUrl(token);
                        if (!string.IsNullOrWhiteSpace(clean)) final.Add((clean, null));
                    }
                }
                final = final.Take(6).ToList();

                if (final.Count > 0)
                {
                    imgUrl = final[0].url;
                    // Resolve the cover thumbnail in priority order:
                    //  1. freshly uploaded cover -> its generated thumbnail
                    //  2. client preserved the original primary -> the client-provided thumbnail
                    //  3. an existing image was promoted to cover (no thumbnail anywhere) ->
                    //     generate one from the cover image so the feed shows a real thumbnail
                    //     instead of falling back to the (possibly missing) placeholder.
                    var hasClientThumb = !string.IsNullOrWhiteSpace(request.ThumbnailUrl)
                        && !Ping.Utils.UrlUtils.IsLocalPath(request.ThumbnailUrl);

                    if (final[0].thumb != null)
                    {
                        thumbUrl = final[0].thumb;
                    }
                    else if (hasClientThumb)
                    {
                        thumbUrl = request.ThumbnailUrl;
                    }
                    else
                    {
                        thumbUrl = await imageService.GenerateThumbnailFromUrlAsync(final[0].url, "reviews", userId);
                    }

                    additionalToSave = final.Skip(1).Select(f => f.url).ToList(); // may be empty to clear extras
                }
                else
                {
                    imgUrl = Ping.Utils.UrlUtils.SanitizeUrl(null);
                    thumbUrl = imgUrl;
                    additionalToSave = new List<string>();
                }
            }
            else
            {
                // Legacy behavior: only newly uploaded files are considered.
                if (uploaded.Any())
                {
                    imgUrl = uploaded[0].original;
                    thumbUrl = uploaded[0].thumb;
                    var extras = uploaded.Skip(1).Select(u => u.original).ToList();
                    additionalToSave = extras.Count > 0 ? extras : null;
                }
                else
                {
                    imgUrl = Ping.Utils.UrlUtils.SanitizeUrl(imgUrl);
                    thumbUrl = Ping.Utils.UrlUtils.SanitizeUrl(thumbUrl);
                    additionalToSave = null;
                }
            }

            // Map to DTO
            var dto = new UpdateReviewDto(
                request.Rating,
                request.Content,
                imgUrl,
                thumbUrl,
                request.Tags,
                additionalToSave
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
