using Conquest.Dtos.Common;
using Conquest.Dtos.Friends;
using Conquest.Services.Friends;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Security.Claims;

namespace Conquest.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class FriendsController(IFriendService friendService) : ControllerBase
    {
        [Authorize]
        [HttpGet("friends")]
        public async Task<ActionResult<PaginatedResult<FriendSummaryDto>>> GetMyFriends(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var friends = await friendService.GetMyFriendsAsync(userId, pagination);
            return Ok(friends);
        }

        [Authorize]
        [HttpPost("add/{username}")]
        public async Task<IActionResult> AddFriend(string username)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await friendService.AddFriendAsync(userId, username);
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

        [Authorize]
        [HttpPost("accept/{username}")]
        public async Task<IActionResult> AcceptFriend(string username)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await friendService.AcceptFriendAsync(userId, username);
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

        [Authorize]
        [HttpPost("requests/incoming")]
        public async Task<ActionResult<PaginatedResult<FriendSummaryDto>>> GetIncomingRequests(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var requests = await friendService.GetIncomingRequestsAsync(userId, pagination);
            return Ok(requests);
        }

        [Authorize]
        [HttpPost("remove/{username}")]
        public async Task<IActionResult> RemoveFriend(string username)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await friendService.RemoveFriendAsync(userId, username);
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
    }
}
