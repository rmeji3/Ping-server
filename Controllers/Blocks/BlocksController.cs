using Conquest.Models.AppUsers;
using Conquest.Services.Blocks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Security.Claims;

namespace Conquest.Controllers.Blocks
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/blocks")]
    [Route("api/v{version:apiVersion}/blocks")]
    [Authorize]
    public class BlocksController(IBlockService blockService) : ControllerBase
    {
        [HttpPost("{userId}")]
        public async Task<IActionResult> BlockUser(string userId)
        {
            var blockerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (blockerId == null) return Unauthorized();

            try
            {
                await blockService.BlockUserAsync(blockerId, userId);
                return Ok(new { message = "User blocked successfully." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{userId}")]
        public async Task<IActionResult> UnblockUser(string userId)
        {
            var blockerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (blockerId == null) return Unauthorized();

            await blockService.UnblockUserAsync(blockerId, userId);
            return Ok(new { message = "User unblocked successfully." });
        }

        [HttpGet]
        public async Task<ActionResult<List<AppUser>>> GetBlockedUsers()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var blockedUsers = await blockService.GetBlockedUsersAsync(userId);
            return Ok(blockedUsers);
        }
    }
}
