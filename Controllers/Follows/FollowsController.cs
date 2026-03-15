using System.Security.Claims;
using Ping.Dtos.Common;
using Ping.Dtos.Friends;
using Ping.Services.Follows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Ping.Controllers.Follows
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Authorize]
    public class FollowsController(IFollowService followService) : ControllerBase
    {
        // POST /api/follows/{userId}
        [HttpPost("{targetId}")]
        public async Task<IActionResult> FollowUser(string targetId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await followService.FollowUserAsync(userId, targetId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // DELETE /api/follows/{userId}
        [HttpDelete("{targetId}")]
        public async Task<IActionResult> UnfollowUser(string targetId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await followService.UnfollowUserAsync(userId, targetId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // GET /api/follows/followers
        [HttpGet("followers")]
        public async Task<ActionResult<PaginatedResult<FriendSummaryDto>>> GetFollowers(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await followService.GetFollowersAsync(userId, pagination);
            return Ok(result);
        }

        // GET /api/follows/following
        [HttpGet("following")]
        public async Task<ActionResult<PaginatedResult<FriendSummaryDto>>> GetFollowing(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await followService.GetFollowingAsync(userId, pagination);
            return Ok(result);
        }

        // GET /api/follows/mutuals (My Friends)
        [HttpGet("mutuals")]
        public async Task<ActionResult<PaginatedResult<FriendSummaryDto>>> GetMutuals(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await followService.GetMutualsAsync(userId, pagination);
            return Ok(result);
        }

        // GET /api/follows/{targetId}/followers
        [HttpGet("{targetId}/followers")]
        public async Task<ActionResult<PaginatedResult<FriendSummaryDto>>> GetUserFollowers(
            string targetId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await followService.GetFollowersAsync(targetId, pagination);
            return Ok(result);
        }

        // GET /api/follows/{targetId}/following
        [HttpGet("{targetId}/following")]
        public async Task<ActionResult<PaginatedResult<FriendSummaryDto>>> GetUserFollowing(
            string targetId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await followService.GetFollowingAsync(targetId, pagination);
            return Ok(result);
        }

        // GET /api/follows/{targetId}/status
        [HttpGet("{targetId}/status")]
        public async Task<ActionResult<bool>> GetFollowStatus(string targetId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var isFollowing = await followService.IsFollowingAsync(userId, targetId);
            return Ok(isFollowing);
        }
    }
}
