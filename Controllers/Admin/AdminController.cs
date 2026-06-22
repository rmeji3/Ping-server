using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ping.Dtos.Auth;
using Ping.Data.Auth;
using Asp.Versioning;
using System.Security.Claims;
using Ping.Services.Pings;
using Ping.Services.Reviews;
using Ping.Services.Events;
using Ping.Services.Activities;
using Ping.Services.Tags;
using Ping.Services.Moderation;
using Ping.Services;
using Ping.Services.Auth;
using Ping.Services.Stickers;
using Ping.Dtos.Business;
using Ping.Dtos.Tags;
using Ping.Dtos.Verification;
using Ping.Dtos.Stickers;
using Ping.Services.Verification;
using Ping.Dtos.Common;
using Ping.Models.AppUsers;

namespace Ping.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/admin")]
    [Route("api/v{version:apiVersion}/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController(
        IPingService pingService,
        IReviewService reviewService,
        IEventService eventService,
        IPingActivityService activityService,
        ITagService tagService,
        IBanningService banningService,
        IBusinessService businessService,
        IVerificationService verificationService,
        IAuthService authService,
        IStickerService stickerService,

        Microsoft.AspNetCore.Identity.UserManager<AppUser> userManager,
        Ping.Services.Admin.IDbJanitorService janitorService,
        AuthDbContext authDbContext,
        Ping.Services.Admin.IAnnouncementService announcementService
        ) : ControllerBase
    {
        // ==========================================
        // Resource Deletion
        // ==========================================

        [HttpGet("pings/search")]
        public async Task<IActionResult> SearchPings([FromQuery] string q, [FromQuery] int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Query parameter 'q' is required." });

            var search = q.Trim().ToLowerInvariant();
            var results = await pingService.SearchPingsAsync(
                new Ping.Dtos.Pings.PingSearchFilterDto { Query = q, PageSize = limit * 3, PageNumber = 1 },
                userId: null);

            // Prioritise name matches over address-only matches
            var items = results.Items
                .OrderByDescending(p => p.Name != null && p.Name.ToLowerInvariant().Contains(search))
                .Take(limit)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Address,
                    GenreName = p.PingGenreName,
                });

            return Ok(items);
        }

        [HttpDelete("pings/{id}")]
        public async Task<IActionResult> DeletePing(int id)
        {
            await pingService.DeletePingAsAdminAsync(id);
            return Ok(new { message = $"Ping {id} deleted." });
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
            await activityService.DeletePingActivityAsAdminAsync(id);
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
        
        [HttpDelete("users/by-identifier")]
        public async Task<IActionResult> DeleteUserByIdentifier([FromQuery] string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) 
                return BadRequest("Identifier (email or username) is required.");

            try
            {
                await authService.DeleteAccountByEmailOrUsernameAsync(identifier);
                return Ok(new { message = $"User '{identifier}' deleted successfully." });
            }
            catch (KeyNotFoundException)
            {
                return NotFound($"User with identifier '{identifier}' not found.");
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

        [HttpPost("users/remove-admin")]
        public async Task<IActionResult> RemoveAdmin([FromQuery] string email)
        {
            try 
            {
                await authService.RemoveAdminAsync(email);
                return Ok(new { message = $"User {email} is no longer an Admin." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpGet("users/banned")]
        public async Task<IActionResult> GetBannedUsers(
            [FromQuery] int page = 1, 
            [FromQuery] int limit = 20,
            [FromQuery] string? userId = null,
            [FromQuery] string? username = null,
            [FromQuery] string? email = null)
        {
            // If searching for specific user
            if (!string.IsNullOrWhiteSpace(userId) || !string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(email))
            {
                var user = await banningService.GetBannedUserAsync(userId, username, email);
                if (user == null) return NotFound("Banned user not found matching the criteria.");
                return Ok(user);
            }

            // Otherwise list all
            var result = await banningService.GetBannedUsersAsync(page, limit);
            return Ok(result);
        }

        [HttpGet("users")]
        public async Task<ActionResult<PaginatedResult<UserDto>>> GetUsers(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string? search = null)
        {
            var query = userManager.Users;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.Trim().ToLower();
                query = query.Where(u => 
                    (u.UserName != null && u.UserName.ToLower().Contains(lowerSearch)) ||
                    (u.Email != null && u.Email.ToLower().Contains(lowerSearch)) ||
                    (u.FirstName != null && u.FirstName.ToLower().Contains(lowerSearch)) ||
                    (u.LastName != null && u.LastName.ToLower().Contains(lowerSearch))
                );
            }

            query = query.OrderBy(u => u.UserName);

            var count = await query.CountAsync();
            var items = await query.Skip((page - 1) * limit).Take(limit).ToListAsync();

            // Resolve all roles in a single batch query to prevent N+1 lag
            var userIds = items.Select(u => u.Id).ToList();
            var rolesQuery = from userRole in authDbContext.UserRoles
                             join role in authDbContext.Roles on userRole.RoleId equals role.Id
                             where userIds.Contains(userRole.UserId)
                             select new { userRole.UserId, RoleName = role.Name };

            var rolesList = await rolesQuery.ToListAsync();
            var rolesLookup = rolesList
                .GroupBy(ur => ur.UserId)
                .ToDictionary(g => g.Key, g => g.Select(ur => ur.RoleName).ToArray());

            var userDtos = items.Select(user => new UserDto(
                user.Id,
                user.Email ?? "",
                user.UserName!,
                user.ProfileImageUrl,
                rolesLookup.TryGetValue(user.Id, out var roles) ? roles! : Array.Empty<string>(),
                user.TwoFactorEnabled
            )).ToList();

            return Ok(new PaginatedResult<UserDto>(userDtos, count, page, limit));
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

        // ==========================================
        // Verification Requests
        // ==========================================

        [HttpGet("verification/requests")]
        public async Task<ActionResult<PaginatedResult<VerificationRequestDto>>> GetVerificationRequests(
            [FromQuery] int page = 1, 
            [FromQuery] int limit = 20)
        {
            var result = await verificationService.GetPendingRequestsAsync(page, limit);
            return Ok(result);
        }

        [HttpPost("verification/{id}/approve")]
        public async Task<IActionResult> ApproveVerification(int id)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (adminId == null) return Unauthorized();

            await verificationService.ApproveRequestAsync(id, adminId);
            return Ok(new { message = "Request approved." });
        }

        [HttpPost("verification/{id}/reject")]
        public async Task<IActionResult> RejectVerification(int id, [FromBody] RejectVerificationDto dto)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (adminId == null) return Unauthorized();

            await verificationService.RejectRequestAsync(id, adminId, dto.Reason);
            return Ok(new { message = "Request rejected." });
        }

        // ==========================================
        // Janitor Tasks
        // ==========================================

        [HttpPost("janitor/cleanup-urls")]
        public async Task<ActionResult<Ping.Services.Admin.JanitorResult>> RunJanitor()
        {
            var result = await janitorService.CleanupFileUrlsAsync();
            return Ok(result);
        }

        // ==========================================
        // Stickers Management
        // ==========================================

        [HttpPost("stickers")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<StickerDto>> CreateSticker(
            [FromForm] string key,
            [FromForm] string name,
            [FromForm] string? category,
            IFormFile? file)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (adminId == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
                return BadRequest("Sticker Key and Name are required.");

            try
            {
                var sticker = await stickerService.CreateStickerAsync(key, name, category, file, adminId);
                return CreatedAtAction(nameof(GetStickerById), new { id = sticker.Id }, sticker);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("stickers")]
        public async Task<ActionResult<List<StickerDto>>> GetAllStickers()
        {
            var stickers = await stickerService.GetAllStickersForAdminAsync();
            return Ok(stickers);
        }

        [HttpGet("stickers/{id}")]
        public async Task<ActionResult<StickerDto>> GetStickerById(string id)
        {
            var sticker = await stickerService.GetStickerByIdAsync(id);
            if (sticker == null) return NotFound();
            return Ok(sticker);
        }

        [HttpPut("stickers/{id}/toggle")]
        public async Task<ActionResult<StickerDto>> ToggleSticker(string id)
        {
            try
            {
                var sticker = await stickerService.ToggleStickerActiveAsync(id);
                return Ok(sticker);
            }
            catch (KeyNotFoundException)
            {
                return NotFound($"Sticker with ID {id} not found.");
            }
        }

        [HttpPut("stickers/{id}/rotation")]
        public async Task<ActionResult<StickerDto>> SetStickerRotation(string id, [FromBody] StickerRotationRequest request)
        {
            try
            {
                var sticker = await stickerService.SetStickerRotationAsync(id, request.InRotation);
                return Ok(sticker);
            }
            catch (KeyNotFoundException)
            {
                return NotFound($"Sticker with ID {id} not found.");
            }
        }

        [HttpDelete("stickers/{id}")]
        public async Task<IActionResult> DeleteSticker(string id)
        {
            try
            {
                await stickerService.DeleteStickerAsync(id);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound($"Sticker with ID {id} not found.");
            }
        }

        [HttpPost("users/{userIdentifier}/stickers/{stickerId}")]
        public async Task<IActionResult> GrantSticker(string userIdentifier, string stickerId)
        {
            try
            {
                await stickerService.GrantStickerOwnershipAsync(userIdentifier, stickerId);
                return Ok(new { message = "Sticker ownership granted successfully." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPost("announcement")]
        public async Task<IActionResult> SetAnnouncement([FromBody] AnnouncementRequest request)
        {
            await announcementService.SetAnnouncementAsync(request.Message);
            return Ok(new { message = "Announcement updated successfully." });
        }
    }

    public class AnnouncementRequest
    {
        public string? Message { get; set; }
    }

    public class StickerRotationRequest
    {
        public bool InRotation { get; set; }
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

