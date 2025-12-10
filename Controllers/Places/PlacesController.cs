using Conquest.Dtos.Activities;
using Conquest.Dtos.Common;
using Microsoft.AspNetCore.Authorization;

namespace Conquest.Controllers.Places
{
    using System.Security.Claims;
    using Conquest.Dtos.Places;
    using Conquest.Models.Places;
    using Conquest.Services.Places;
    using Microsoft.AspNetCore.Mvc;

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PlacesController(IPlaceService placeService, Conquest.Services.Business.IBusinessAnalyticsService analyticsService) : ControllerBase
    {
        // POST /api/places
        [HttpPost]
        public async Task<ActionResult<PlaceDetailsDto>> Create([FromBody] UpsertPlaceDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Place name is required.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to create a place.");

            try
            {
                var result = await placeService.CreatePlaceAsync(dto, userId);
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
        
        // PUT /api/places/{id}
        [HttpPut("{id:int}")]
        public async Task<ActionResult<PlaceDetailsDto>> Update(int id, [FromBody] UpsertPlaceDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Place name is required.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to update a place.");
            
            try
            {
                var result = await placeService.UpdatePlaceAsync(id, dto, userId);
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

        // GET /api/places/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PlaceDetailsDto>> GetById(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await placeService.GetPlaceByIdAsync(id, userId);

            if (result is null) return NotFound();

            return Ok(result);
        }

        // POST /api/places/{id}/view
        [HttpPost("{id:int}/view")]
        [AllowAnonymous] // Allow unauthenticated views? "tapping on place". Let's say yes for now, or match controller
        public async Task<ActionResult> TrackView(int id)
        {
            await analyticsService.TrackPlaceViewAsync(id);
            return Ok();
        }

        // GET /api/places/nearby?lat=..&lng=..&radiusKm=5&activityName=soccer&activityKind=outdoor&visibility=Public&type=Verified
        [HttpGet("nearby")]
        public async Task<ActionResult<PaginatedResult<PlaceDetailsDto>>> Nearby(
            [FromQuery] double lat,
            [FromQuery] double lng,
            [FromQuery] double radiusKm = 5,
            [FromQuery] string? activityName = null,
            [FromQuery] string? activityKind = null,
            [FromQuery] PlaceVisibility? visibility = null,
            [FromQuery] PlaceType? type = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await placeService.SearchNearbyAsync(lat, lng, radiusKm, activityName, activityKind, visibility, type, userId, pagination);
            return Ok(result);
        }

        // POST /api/places/favorited/{id}
        [HttpPost("favorited/{id:int}")]
        public async Task<ActionResult> AddFavorite(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to favorite a place.");

            try
            {
                await placeService.AddFavoriteAsync(id, userId);
                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // DELETE /api/places/favorited/{id}
        [HttpDelete("favorited/{id:int}")]
        public async Task<ActionResult> Unfavorite(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to unfavorite a place.");

            await placeService.UnfavoriteAsync(id, userId);
            return NoContent();
        }

        // GET /api/places/favorited?pageNumber=1&pageSize=20
        [HttpGet("favorited")]
        public async Task<ActionResult<PaginatedResult<PlaceDetailsDto>>> GetFavorited(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to view favorited places.");

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await placeService.GetFavoritedPlacesAsync(userId, pagination);
            return Ok(result);
        }

        // POST /api/places/{id}/delete
        [HttpPost("{id:int}/delete")]
        public async Task<ActionResult> Delete(int id)
        {
           var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
           if (userId is null)
               return Unauthorized("You must be logged in to delete a place.");

           await placeService.DeletePlaceAsync(id, userId);
           return NoContent();
        }
    }
}
