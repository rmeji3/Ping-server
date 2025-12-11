using Conquest.Dtos.Business;
using Conquest.Models.Business;
using Conquest.Services;
using Conquest.Services.Business;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Security.Claims;

namespace Conquest.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/business")]
    [Route("api/v{version:apiVersion}/business")]
    public class BusinessController : ControllerBase
    {
        private readonly IBusinessService _businessService;
        private readonly IBusinessAnalyticsService _analyticsService;
        private readonly Conquest.Services.Places.IPlaceService _placeService;

        public BusinessController(IBusinessService businessService, IBusinessAnalyticsService analyticsService, Conquest.Services.Places.IPlaceService placeService)
        {
            _businessService = businessService;
            _analyticsService = analyticsService;
            _placeService = placeService;
        }

        [HttpPost("claim")]
        [Authorize]
        public async Task<ActionResult<PlaceClaim>> SubmitClaim([FromBody] CreateClaimDto dto)
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

        [HttpGet("analytics/{placeId}")]
        [Authorize(Roles = "Business,Admin,User")] // User role allowed if they are the owner (logic inside service or check here)
        public async Task<IActionResult> GetAnalytics(int placeId)
        {
            // Security: In a real app, verify user owns the place.
            // For now, assuming frontend sending correct PlaceId for logged in user.
            try 
            {
                var stats = await _analyticsService.GetPlaceAnalyticsAsync(placeId);
                return Ok(stats);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("Place not found");
            }
        }

        [HttpGet("places")]
        [Authorize]
        public async Task<IActionResult> GetMyPlaces()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var places = await _placeService.GetPlacesByOwnerAsync(userId, onlyClaimed: true);
            return Ok(places);
        }
    }
}
