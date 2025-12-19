using Ping.Dtos.Business;
using Ping.Models.Business;
using Ping.Services;
using Ping.Services.Business;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Security.Claims;

namespace Ping.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/business")]
    [Route("api/v{version:apiVersion}/business")]
    public class BusinessController : ControllerBase
    {
        private readonly IBusinessService _businessService;
        private readonly IBusinessAnalyticsService _analyticsService;
        private readonly Ping.Services.Pings.IPingService _pingService;
    
        public BusinessController(IBusinessService businessService, IBusinessAnalyticsService analyticsService, Ping.Services.Pings.IPingService pingService)
        {
            _businessService = businessService;
            _analyticsService = analyticsService;
            _pingService = pingService;
        }

        [HttpPost("claim")]
        [Authorize]
        public async Task<ActionResult<PingClaim>> SubmitClaim([FromBody] CreateClaimDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            try
            {
                var claim = await _businessService.SubmitClaimAsync(userId, dto);
                return Ok(claim);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpGet("analytics/{pingId}")]
        [Authorize(Roles = "Business,Admin,User")] // User role allowed if they are the owner (logic inside service or check here)
        public async Task<IActionResult> GetAnalytics(int pingId)
        {
            // Security: In a real app, verify user owns the place.
            // For now, assuming frontend sending correct PlaceId for logged in user.
            try 
            {
                var stats = await _analyticsService.GetPingAnalyticsAsync(pingId);
                return Ok(stats);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("Ping not found");
            }
        }

        [HttpGet("pings")]
        [Authorize]
        public async Task<IActionResult> GetMyPings()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var pings = await _pingService.GetPingsByOwnerAsync(userId, onlyClaimed: true);
            return Ok(pings);
        }
    }
}

