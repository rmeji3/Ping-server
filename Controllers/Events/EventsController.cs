using System.Security.Claims;
using Ping.Dtos.Common;
using Ping.Dtos.Events;
using Ping.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Ping.Controllers.Events
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
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
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] int? genreId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            if (!lat.HasValue || !lng.HasValue || !radiusKm.HasValue || radiusKm <= 0)
            {
                return BadRequest("lat, lng, and radiusKm are required.");
            }

            var filter = new EventFilterDto
            {
                Latitude = lat,
                Longitude = lng,
                RadiusKm = radiusKm,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                FromDate = fromDate,
                ToDate = toDate,
                GenreId = genreId
            };

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var result = await eventService.GetPublicEventsAsync(filter, pagination, userId);

            return Ok(result);
        }

        // GET /api/events/place/{placeId}?pageNumber=1&pageSize=20
        [HttpGet("ping/{pingId:int}")]
        public async Task<ActionResult<PaginatedResult<EventDto>>> GetByPing(
            int pingId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await eventService.GetEventsByPingAsync(pingId, userId, pagination);
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

        // POST /api/events/{id}/attend
        [HttpPost("{id:int}/attend")]
        public async Task<IActionResult> AttendEvent(int id)
        {
            // Reuse Join logic
            return await JoinEvent(id);
        }

        public record InviteRequest(string UserId);

        // POST /api/events/{id}/invite
        [HttpPost("{id:int}/invite")]
        public async Task<IActionResult> InviteUser(int id, [FromBody] InviteRequest req)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            if (string.IsNullOrEmpty(req.UserId)) return BadRequest("UserId is required.");

            try
            {
                await eventService.InviteUserAsync(id, userId, req.UserId);
                return Ok("User invited.");
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
        }

        // POST /api/events/{id}/uninvite
        [HttpPost("{id:int}/uninvite")]
        public async Task<IActionResult> UninviteUser(int id, [FromBody] InviteRequest req)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            if (string.IsNullOrEmpty(req.UserId)) return BadRequest("UserId is required.");

            try
            {
                var result = await eventService.UninviteUserAsync(id, userId, req.UserId);
                if (!result) return NotFound("User not found in event list.");
                return Ok("User uninvited.");
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
        }

        // GET /api/events/{id}/invite-candidates
        [HttpGet("{id:int}/invite-candidates")]
        public async Task<ActionResult<PaginatedResult<FriendInviteDto>>> GetInviteCandidates(
            int id,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await eventService.GetFriendsToInviteAsync(id, userId, pagination);
            return Ok(result);
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
        // POST /api/events/{id}/comments
        [HttpPost("{id:int}/comments")]
        public async Task<ActionResult<EventCommentDto>> AddComment(int id, [FromBody] CreateEventCommentDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await eventService.AddCommentAsync(id, userId, dto.Content);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("Event not found.");
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // GET /api/events/{id}/comments?pageNumber=1&pageSize=20
        [HttpGet("{id:int}/comments")]
        public async Task<ActionResult<PaginatedResult<EventCommentDto>>> GetComments(
            int id,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var result = await eventService.GetCommentsAsync(id, pagination);
            return Ok(result);
        }

        // PATCH /api/events/comments/{commentId}
        [HttpPatch("comments/{commentId:int}")]
        public async Task<ActionResult<EventCommentDto>> UpdateComment(int commentId, [FromBody] UpdateEventCommentDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await eventService.UpdateCommentAsync(commentId, userId, dto.Content);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("Comment not found.");
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

        // DELETE /api/events/comments/{commentId}
        [HttpDelete("comments/{commentId:int}")]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await eventService.DeleteCommentAsync(commentId, userId);
                if (!result) return NotFound("Comment not found.");
                return Ok("Comment deleted.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
        }
    }
}
