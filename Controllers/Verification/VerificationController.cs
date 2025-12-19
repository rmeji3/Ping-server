using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Security.Claims;
using Ping.Services.Verification;
using Ping.Dtos.Verification;

namespace Ping.Controllers.Verification
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/verification")]
    [Route("api/v{version:apiVersion}/verification")]
    [Authorize]
    public class VerificationController(IVerificationService verificationService) : ControllerBase
    {
        [HttpPost("apply")]
        public async Task<IActionResult> Apply()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            try
            {
                await verificationService.ApplyAsync(userId);
                return Ok(new { message = "Verification application submitted." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("status")]
        public async Task<ActionResult<VerificationStatus?>> GetStatus()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var status = await verificationService.GetUserVerificationStatusAsync(userId);
            return Ok(new { status });
        }
    }
}
