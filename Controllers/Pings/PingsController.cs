using Ping.Dtos.Activities;
using Ping.Dtos.Common;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Ping.Dtos.Pings;
using Ping.Models.Pings;
using Ping.Services.Pings;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Ping.Controllers.Pings
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Authorize]
    public class PingsController(IPingService pingService, Ping.Services.Business.IBusinessAnalyticsService analyticsService) : ControllerBase
    {
        // POST /api/pings
        [HttpPost]
        public async Task<ActionResult<PingDetailsDto>> Create([FromBody] UpsertPingDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Ping name is required.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to create a ping.");

            try
            {
                var result = await pingService.CreatePingAsync(dto, userId);
                return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
        
        // PUT /api/pings/{id}
        [HttpPut("{id:int}")]
        public async Task<ActionResult<PingDetailsDto>> Update(int id, [FromBody] UpsertPingDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Ping name is required.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to update a ping.");
            
            try
            {
                var result = await pingService.UpdatePingAsync(id, dto, userId);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // GET /api/pings/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PingDetailsDto>> GetById(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await pingService.GetPingByIdAsync(id, userId);

            if (result is null) return NotFound();

            return Ok(result);
        }

        // POST /api/pings/{id}/view
        [HttpPost("{id:int}/view")]
        [AllowAnonymous] 
        public async Task<ActionResult> TrackView(int id)
        {
            await analyticsService.TrackPingViewAsync(id);
            return Ok();
        }

        // GET /api/pings/nearby
        [HttpGet("nearby")]
        public async Task<ActionResult<PaginatedResult<PingDetailsDto>>> Nearby(
            [FromQuery] double lat,
            [FromQuery] double lng,
            [FromQuery] double radiusKm = 5,
            [FromQuery] string? activityName = null,
            [FromQuery] string? pingGenreName = null,
            [FromQuery] PingVisibility? visibility = null,
            [FromQuery] PingType? type = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await pingService.SearchNearbyAsync(lat, lng, radiusKm, activityName, pingGenreName, visibility, type, userId, pagination);
            return Ok(result);
        }

        // POST /api/pings/favorited/{id}
        [HttpPost("favorited/{id:int}")]
        public async Task<ActionResult> AddFavorite(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to favorite a ping.");

            try
            {
                await pingService.AddFavoriteAsync(id, userId);
                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // DELETE /api/pings/favorited/{id}
        [HttpDelete("favorited/{id:int}")]
        public async Task<ActionResult> Unfavorite(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to unfavorite a ping.");

            await pingService.UnfavoriteAsync(id, userId);
            return NoContent();
        }

        // GET /api/pings/favorited
        [HttpGet("favorited")]
        public async Task<ActionResult<PaginatedResult<PingDetailsDto>>> GetFavorited(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to view favorited pings.");

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await pingService.GetFavoritedPingsAsync(userId, pagination);
            return Ok(result);
        }

        // POST /api/pings/{id}/delete
        [HttpPost("{id:int}/delete")]
        public async Task<ActionResult> Delete(int id)
        {
           var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
           if (userId is null)
               return Unauthorized("You must be logged in to delete a ping.");

           await pingService.DeletePingAsync(id, userId);
           return NoContent();
        }
    }
}

