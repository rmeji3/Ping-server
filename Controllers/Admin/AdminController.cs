using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Security.Claims;
using Conquest.Services.Places;
using Conquest.Services.Reviews;
using Conquest.Services.Events;
using Conquest.Services.Activities;
using Conquest.Services.Tags;
using Conquest.Services.Moderation;
using Conquest.Services;
using Conquest.Services.Auth;
using Conquest.Dtos.Business;
using Conquest.Dtos.Tags;
using Conquest.Models.AppUsers;

namespace Conquest.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/admin")]
    [Route("api/v{version:apiVersion}/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController(
        IPlaceService placeService,
        IReviewService reviewService,
        IEventService eventService,
        IActivityService activityService,
        ITagService tagService,
        IBanningService banningService,
        IBusinessService businessService,
        IAuthService authService,

        Microsoft.AspNetCore.Identity.UserManager<AppUser> userManager
        ) : ControllerBase
    {
        // ==========================================
        // Resource Deletion
        // ==========================================

        [HttpDelete("places/{id}")]
        public async Task<IActionResult> DeletePlace(int id)
        {
            await placeService.DeletePlaceAsAdminAsync(id);
            return Ok(new { message = $"Place {id} deleted." });
        }

        [HttpDelete("reviews/{id}")]
        public async Task<IActionResult> DeleteReview(int id)
        {
            await reviewService.DeleteReviewAsAdminAsync(id);
            return Ok(new { message = $"Review {id} deleted." });
        }

        [HttpDelete("events/{id}")]
        public async Task<IActionResult> DeleteEvent(int id)
        {
            await eventService.DeleteEventAsAdminAsync(id);
            return Ok(new { message = $"Event {id} deleted." });
        }

        [HttpDelete("activities/{id}")]
        public async Task<IActionResult> DeleteActivity(int id)
        {
            await activityService.DeleteActivityAsAdminAsync(id);
            return Ok(new { message = $"Activity {id} deleted." });
        }

        [HttpDelete("tags/{id}")]
        public async Task<IActionResult> DeleteTag(int id)
        {
            await tagService.DeleteTagAsAdminAsync(id);
            return Ok(new { message = $"Tag {id} deleted." });
        }

        // ==========================================
        // Users & Moderation
        // ==========================================

        [HttpPost("users/{id}/ban")]
        public async Task<IActionResult> BanUser(string id, [FromQuery] string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return BadRequest("Reason is required.");

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await banningService.BanUserAsync(id, reason, adminId);
            return Ok(new { message = $"User {id} banned." });
        }

        [HttpPost("users/ban")]
        public async Task<IActionResult> BanUserByUsername([FromQuery] string username, [FromQuery] string reason)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(reason))
                return BadRequest("Username and Reason are required.");

            var user = await userManager.FindByNameAsync(username);
            if (user == null)
                return NotFound($"User with username '{username}' not found.");

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await banningService.BanUserAsync(user.Id, reason, adminId);
            return Ok(new { message = $"User {username} banned." });
        }

        [HttpPost("users/{id}/unban")]
        public async Task<IActionResult> UnbanUser(string id)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await banningService.UnbanUserAsync(id, adminId);
            return Ok(new { message = $"User {id} unbanned." });
        }

        [HttpPost("users/unban")]
        public async Task<IActionResult> UnbanUserByUsername([FromQuery] string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("Username is required.");

            var user = await userManager.FindByNameAsync(username);
            if (user == null)
                return NotFound($"User with username '{username}' not found.");

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await banningService.UnbanUserAsync(user.Id, adminId);
            return Ok(new { message = $"User {username} unbanned." });
        }
        
        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            try
            {
                await authService.DeleteAccountAsync(id);
                return Ok(new { message = $"User {id} deleted successfully." });
            }
            catch (KeyNotFoundException)
            {
                return NotFound($"User with ID {id} not found.");
            }
        }
        
        [HttpPost("users/make-admin")]
        public async Task<IActionResult> MakeAdmin([FromQuery] string email)
        {
            try 
            {
                await authService.MakeAdminAsync(email);
                return Ok(new { message = $"User {email} is now an Admin." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPost("moderation/ip/ban")]
        public async Task<IActionResult> BanIp([FromBody] IpBanRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Ip) || string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest("IP and Reason are required.");

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await banningService.BanIpAsync(request.Ip, request.Reason, request.ExpiresAt, adminId);
            return Ok(new { message = $"IP {request.Ip} banned." });
        }

        [HttpPost("moderation/ip/unban")]
        public async Task<IActionResult> UnbanIp([FromBody] IpUnbanRequest request)
        {
             if (string.IsNullOrWhiteSpace(request.Ip))
                return BadRequest("IP is required.");

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await banningService.UnbanIpAsync(request.Ip, adminId);
            return Ok(new { message = $"IP {request.Ip} unbanned." });
        }

        // ==========================================
        // Business Claims
        // ==========================================

        [HttpGet("business/claims")]
        public async Task<ActionResult<List<ClaimDto>>> GetPendingClaims()
        {
            var claims = await businessService.GetPendingClaimsAsync();
            return Ok(claims);
        }

        [HttpPost("business/claims/{id}/approve")]
        public async Task<IActionResult> ApproveClaim(int id)
        {
            var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (reviewerId == null) return Unauthorized(); // Should ensure admin claim

            try
            {
                await businessService.ApproveClaimAsync(id, reviewerId);
                return Ok(new { message = "Claim approved and ownership transferred." });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("business/claims/{id}/reject")]
        public async Task<IActionResult> RejectClaim(int id)
        {
            var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (reviewerId == null) return Unauthorized();

            try
            {
                await businessService.RejectClaimAsync(id, reviewerId);
                return Ok(new { message = "Claim rejected." });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ==========================================
        // Tags Management
        // ==========================================

        [HttpPost("tags/{id}/approve")]
        public async Task<IActionResult> ApproveTag(int id)
        {
            await tagService.ApproveTagAsync(id);
            return Ok();
        }

        [HttpPost("tags/{id}/ban")]
        public async Task<IActionResult> BanTag(int id)
        {
            await tagService.BanTagAsync(id);
            return Ok();
        }

        [HttpPost("tags/{id}/merge/{targetId}")]
        public async Task<IActionResult> MergeTag(int id, int targetId)
        {
            await tagService.MergeTagAsync(id, targetId);
            return Ok();
        }
    }

    public class IpBanRequest
    {
        public required string Ip { get; set; }
        public required string Reason { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public class IpUnbanRequest
    {
         public required string Ip { get; set; }
    }
}
