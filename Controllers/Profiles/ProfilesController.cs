using System.Security.Claims;
using Ping.Dtos.Profiles;
using Ping.Services.Profiles;
using Ping.Services.Reviews;
using Ping.Services.Moderation;
using Ping.Dtos.Common;
using Ping.Dtos.Reviews;
using Ping.Dtos.Pings;
using Ping.Dtos.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Ping.Models.AppUsers;

namespace Ping.Controllers.Profiles
{

    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Authorize]
    public class ProfilesController(IProfileService profileService, IReviewService reviewService, IModerationService moderationService) : ControllerBase
    {
        // GET /api/profiles/me
        [HttpGet("me")]
        public async Task<ActionResult<PersonalProfileDto>> Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var profile = await profileService.GetMyProfileAsync(userId);
                return Ok(profile);
            }
            catch (KeyNotFoundException)
            {
                return Unauthorized();
            }
        }
        
        // GET /api/profiles/search?username=someUsername&pageNumber=1&pageSize=10
        [HttpGet("search")]
        public async Task<ActionResult<PaginatedResult<ProfileDto>>> Search([FromQuery] string username, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 15)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var users = await profileService.SearchProfilesAsync(username, userId, pagination);
                return Ok(users);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // GET /api/profiles/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<ProfileDto>> GetProfile(string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                var profile = await profileService.GetProfileByIdAsync(id, currentUserId);
                return Ok(profile);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }

        // GET /api/profiles/{id}/summary
        [HttpGet("{id}/summary")]
        public async Task<ActionResult<QuickProfileDto>> GetQuickProfile(string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                var profile = await profileService.GetQuickProfileAsync(id, currentUserId);
                return Ok(profile);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }

        // GET /api/profiles/{id}/reviews?pageNumber=1&pageSize=10
        [HttpGet("{id}/reviews")]
        public async Task<ActionResult<PaginatedResult<ExploreReviewDto>>> GetUserReviews(string id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            // 1. Check Privacy (Can I see reviews?)
            // Reuse ProfileService logic? Or just duplicate simple check?
            // Let's call GetProfileByIdAsync to get the privacy status and friendship status.
            // It's a bit heavy but ensures consistency.
            // Optimized way: Add a lightweight "CanViewReviews" method.
            // For now, let's just fetch the profile and check IsFriends + Privacy.
            try
            {
                var profile = await profileService.GetQuickProfileAsync(id, currentUserId);
                
                bool canView = profile.Id == currentUserId || 
                            profile.ReviewsPrivacy == PrivacyConstraint.Public || 
                            (profile.ReviewsPrivacy == PrivacyConstraint.FriendsOnly && profile.IsFriends);

                if (!canView)
                {
                    // Return empty or Forbidden?
                    // Let's return empty list to not leak existence, or forbidden if strict.
                    // Usually empty is safer/cleaner for UI "No reviews".
                    // But if it's explicitly private, UI should know.
                    // The frontend checks permissions via "ReviewsPrivacy" enum.
                    // So if we are here, frontend shouldn't have called it unless allowed.
                    // Backend must enforce.
                    return Forbid();
                }

                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var reviews = await reviewService.GetUserReviewsAsync(id, currentUserId, pagination);
                return Ok(reviews);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }

        // GET /api/profiles/{id}/places?pageNumber=1&pageSize=10
        [HttpGet("{id}/pings")]
        public async Task<ActionResult<PaginatedResult<PingDetailsDto>>> GetUserPings(string id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                // Privacy check is inside the service method
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var places = await profileService.GetUserPingsAsync(id, currentUserId, pagination);
                return Ok(places);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }

        // GET /api/profiles/{id}/events?pageNumber=1&pageSize=10
        [HttpGet("{id}/events")]
        public async Task<ActionResult<PaginatedResult<EventDto>>> GetUserEvents(string id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? sortBy = null, [FromQuery] string? sortOrder = null)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                // Privacy check is inside the service method
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var events = await profileService.GetUserEventsAsync(id, currentUserId, pagination, sortBy, sortOrder);
                return Ok(events);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }
        
        [HttpPatch("me/bio")]
        public async Task<IActionResult> UpdateBio([FromBody] UpdateBioDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            try
            {
                await profileService.UpdateBioAsync(userId, dto.Bio);
                return Ok(new { message = "Bio updated successfully." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("me/image")]
        public async Task<ActionResult<string>> UploadProfileImage(IFormFile file)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            if (file == null || file.Length == 0) return BadRequest("File is empty.");

            // Moderate Image
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                var base64 = Convert.ToBase64String(ms.ToArray());
                var dataUrl = $"data:{file.ContentType};base64,{base64}";
                var moderation = await moderationService.CheckImageAsync(dataUrl);
                if (moderation.IsFlagged)
                    return BadRequest($"Image rejected by moderation: {moderation.Reason}");
            }

            try
            {
                var url = await profileService.UpdateProfileImageAsync(userId, file);
                return Ok(new { Url = url });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception)
            {
                // Log ex
                return StatusCode(500, "An error occurred while uploading the image.");
            }
        }

        // GET /api/profiles/{id}/places?pageNumber=1&pageSize=10
        [HttpGet("{id}/places")]
        public async Task<ActionResult<PaginatedResult<PlaceReviewSummaryDto>>> GetProfilePlaces(string id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? sortBy = null, [FromQuery] string? sortOrder = null)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var places = await profileService.GetProfilePlacesAsync(id, currentUserId, pagination, sortBy, sortOrder);
                return Ok(places);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }

        // GET /api/profiles/me/places?pageNumber=1&pageSize=10
        [HttpGet("me/places")]
        public async Task<ActionResult<PaginatedResult<PlaceReviewSummaryDto>>> GetMyProfilePlaces([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? sortBy = null, [FromQuery] string? sortOrder = null)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                // Pass currentUserId as both target and viewer to bypass privacy checks
                var places = await profileService.GetProfilePlacesAsync(currentUserId, currentUserId, pagination, sortBy, sortOrder);
                return Ok(places);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }

        // GET /api/profiles/{id}/places/{placeId}/reviews?pageNumber=1&pageSize=10
        [HttpGet("{id}/places/{placeId:int}/reviews")]
        public async Task<ActionResult<PaginatedResult<ReviewDto>>> GetProfilePlaceReviews(string id, int placeId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var reviews = await profileService.GetProfilePlaceReviewsAsync(id, placeId, currentUserId, pagination);
                return Ok(reviews);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        // GET /api/profiles/me/places/{placeId}/reviews?pageNumber=1&pageSize=10
        [HttpGet("me/places/{placeId:int}/reviews")]
        public async Task<ActionResult<PaginatedResult<ReviewDto>>> GetMyProfilePlaceReviews(int placeId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var reviews = await profileService.GetProfilePlaceReviewsAsync(currentUserId, placeId, currentUserId, pagination);
                return Ok(reviews);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        // GET /api/profiles/{id}/likes?pageNumber=1&pageSize=10
        [HttpGet("{id}/likes")]
        public async Task<ActionResult<PaginatedResult<ExploreReviewDto>>> GetUserLikes(string id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? sortBy = null, [FromQuery] string? sortOrder = null)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var likes = await reviewService.GetUserLikesAsync(id, currentUserId, pagination, sortBy, sortOrder);
                return Ok(likes);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }

        // GET /api/profiles/me/likes?pageNumber=1&pageSize=10
        [HttpGet("me/likes")]
        public async Task<ActionResult<PaginatedResult<ExploreReviewDto>>> GetMyLikes([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? sortBy = null, [FromQuery] string? sortOrder = null)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var likes = await reviewService.GetLikedReviewsAsync(currentUserId, pagination, sortBy, sortOrder);
            return Ok(likes);
        }

        // PATCH /api/profiles/me/privacy
        [HttpPatch("me/privacy")]
        public async Task<IActionResult> UpdatePrivacy([FromBody] PrivacySettingsDto dto)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                await profileService.UpdateProfilePrivacyAsync(currentUserId, dto);
                return Ok(new { message = "Privacy settings updated." });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}
