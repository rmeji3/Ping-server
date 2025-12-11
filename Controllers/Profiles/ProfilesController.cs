using System.Security.Claims;
using Conquest.Dtos.Profiles;
using Conquest.Services.Profiles;
using Conquest.Services.Reviews;
using Conquest.Dtos.Common;
using Conquest.Dtos.Reviews;
using Conquest.Dtos.Places;
using Conquest.Dtos.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Conquest.Models.AppUsers;

namespace Conquest.Controllers.Profiles
{

    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Authorize]
    public class ProfilesController(IProfileService profileService, IReviewService reviewService) : ControllerBase
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
        
        // GET /api/profiles/search?username=someUsername
        [HttpGet("search")]
        public async Task<ActionResult<List<ProfileDto>>> Search([FromQuery] string username)
        {
            var yourUsername = User.FindFirstValue(ClaimTypes.Name);
            if (yourUsername is null) return Unauthorized();

            try
            {
                var users = await profileService.SearchProfilesAsync(username, yourUsername);
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
        [HttpGet("{id}/places")]
        public async Task<ActionResult<PaginatedResult<PlaceDetailsDto>>> GetUserPlaces(string id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                // Privacy check is inside the service method
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var places = await profileService.GetUserPlacesAsync(id, currentUserId, pagination);
                return Ok(places);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }

        // GET /api/profiles/{id}/events?pageNumber=1&pageSize=10
        [HttpGet("{id}/events")]
        public async Task<ActionResult<PaginatedResult<EventDto>>> GetUserEvents(string id, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId is null) return Unauthorized();

            try
            {
                // Privacy check is inside the service method
                var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
                var events = await profileService.GetUserEventsAsync(id, currentUserId, pagination);
                return Ok(events);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("User not found.");
            }
        }
        
        [HttpPost("me/image")]
        public async Task<ActionResult<string>> UploadProfileImage(IFormFile file)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

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
    }
}