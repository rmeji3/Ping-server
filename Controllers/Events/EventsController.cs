using System.Security.Claims;
using Conquest.Dtos.Common;
using Conquest.Dtos.Events;
using Conquest.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Conquest.Controllers.Events
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EventsController(IEventService eventService) : ControllerBase
    {
        // POST /api/Events/create
        [HttpPost("create")]
        public async Task<ActionResult<EventDto>> Create([FromBody] CreateEventDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await eventService.CreateEventAsync(dto, userId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<EventDto>> GetById(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            // Even if userId is null (not auth?), service handles null securely.
            // But controller has [Authorize], so userId should exist unless token issue.

            var result = await eventService.GetEventByIdAsync(id, userId);
            if (result is null) return NotFound("Event not found.");
            return Ok(result);
        }

        // GET /api/events/mine?pageNumber=1&pageSize=20
        [HttpGet("mine")]
        public async Task<ActionResult<PaginatedResult<EventDto>>> GetMyEvents(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await eventService.GetMyEventsAsync(userId, pagination);
            return Ok(result);
        }

        // GET /api/events/attending?pageNumber=1&pageSize=20
        [HttpGet("attending")]
        public async Task<ActionResult<PaginatedResult<EventDto>>> GetEventsAttending(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await eventService.GetEventsAttendingAsync(userId, pagination);
            return Ok(result);
        }

        // GET /api/events/public?lat=..&lng=..&radiusKm=..&pageNumber=..&pageSize=..
        [HttpGet("public")]
        public async Task<ActionResult<PaginatedResult<EventDto>>> GetPublicEvents(
            [FromQuery] double? lat,
            [FromQuery] double? lng,
            [FromQuery] double? radiusKm,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            if (!lat.HasValue || !lng.HasValue || !radiusKm.HasValue || radiusKm <= 0)
            {
                return BadRequest("lat, lng, and radiusKm are required.");
            }

            var centerLat = lat.Value;
            var centerLng = lng.Value;
            var radius = radiusKm.Value;
            var latDelta = radius / 111.32;
            var lngDelta = radius / (111.32 * Math.Cos(centerLat * Math.PI / 180.0));
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };

            var result = await eventService.GetPublicEventsAsync(
                centerLat - latDelta,
                centerLat + latDelta,
                centerLng - lngDelta,
                centerLng + lngDelta,
                pagination
            );

            return Ok(result);
        }

        // DELETE /api/events/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteEvent(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await eventService.DeleteEventAsync(id, userId);
                if (!result) return NotFound("Event not found.");
                return Ok("Event deleted successfully.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
        }

        // POST /api/events/{id}/join
        [HttpPost("{id:int}/join")]
        public async Task<IActionResult> JoinEvent(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var result = await eventService.JoinEventAsync(id, userId);
            if (!result) return NotFound("Event not found.");
            return Ok("Joined event.");
        }

        // PATCH /api/Events/{id}
        [HttpPatch("{id:int}")]
        public async Task<ActionResult<EventDto>> UpdateEvent(int id, [FromBody] UpdateEventDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await eventService.UpdateEventAsync(id, dto, userId);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("Event not found.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // POST /api/events/{id}/leave
        [HttpPost("{id:int}/leave")]
        public async Task<IActionResult> LeaveEvent(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var result = await eventService.LeaveEventAsync(id, userId);
            if (!result) return NotFound("Not attending this event.");
            return Ok("Left event.");
        }
    }
}