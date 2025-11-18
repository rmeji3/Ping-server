using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Conquest.Data.App;
using Conquest.Dtos.Events;
using Conquest.Models.AppUsers;
using Conquest.Models.Events;
using Conquest.Services.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Conquest.Controllers.Events{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EventsController : Controller {
    
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;
        public EventsController(AppDbContext db, UserManager<AppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // POST /api/Events/create
        [HttpPost("create")]
        public async Task<ActionResult<EventDto>> Create([FromBody] CreateEventDto dto)
        {
            var now = DateTime.UtcNow;
            //5 min grace period
            var gracePeriod = now.AddMinutes(-5);
            
            // rule 1: event must start in the future
            if (dto.StartTime < gracePeriod)
                return BadRequest("Start time cannot be earlier than the current time.");

            // rule 2: end must be after start
            if (dto.EndTime <= dto.StartTime)
                return BadRequest("End time must be later than the start time.");
            
            //rule 3: duration must be at least 15 minutes
            if ((dto.EndTime - dto.StartTime).TotalMinutes < 15)
                return BadRequest("Event duration must be at least 15 minutes.");
            
            //rule 4: title and location cannot be empty or whitespace
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest("Title cannot be empty.");
            if (string.IsNullOrWhiteSpace(dto.Location))
                return BadRequest("Location cannot be empty.");
            
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return Unauthorized();
            var newEvent = new Event
            {
                Title = dto.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description)
                    ? null
                    : dto.Description.Trim(),
                IsPublic = dto.IsPublic,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                Location = dto.Location.Trim(),
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow,
                Attendees = new List<EventAttendee>(),
                Latitude = dto.Latitude,
                Longitude = dto.Longitude
            };
            _db.Events.Add(newEvent);
            await _db.SaveChangesAsync();
            
            var createdBySummary = new UserSummaryDto(
                user.Id,
                user.UserName!,
                user.FirstName,
                user.LastName
            );
            
            var result = new EventDto(
                newEvent.Id,
                newEvent.Title,
                newEvent.Description,
                newEvent.IsPublic,
                newEvent.StartTime,
                newEvent.EndTime,
                newEvent.Location,
                createdBySummary,
                newEvent.CreatedAt,
                new List<UserSummaryDto>(), // no attendees yet
                EventsService.ComputeStatus(newEvent),
                newEvent.Latitude,
                newEvent.Longitude
            );

            // if you don't have GetById yet, you can just do:
            return Ok(result);
        }
        
        // GET /api/Events/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<EventDto>> GetById(int id)
        {
            var ev = await _db.Events
                .Include(e => e.Attendees)
                .FirstOrDefaultAsync(e => e.Id == id);
            
            if (ev == null)
                return NotFound();

            var creator = await _userManager.FindByIdAsync(ev.CreatedById);
            if(creator is null) return BadRequest("Event creator not found.");

            var creatorSummary = new UserSummaryDto(
                creator.Id,
                creator.UserName!,
                creator.FirstName,
                creator.LastName
            );
            
            // Load all attendee users in ONE query
            var attendeeIds = ev.Attendees
                .Select(a => a.UserId)
                .Distinct()
                .ToList();

            var attendeeUsers = await _userManager.Users
                .Where(u => attendeeIds.Contains(u.Id))
                .ToListAsync();

            var usersById = attendeeUsers.ToDictionary(u => u.Id);

            var attendeeSummaries = ev.Attendees
                .Where(a => usersById.ContainsKey(a.UserId))
                .Select(a =>
                {
                    var u = usersById[a.UserId];
                    return new UserSummaryDto(
                        u.Id,
                        u.UserName!,
                        u.FirstName,
                        u.LastName
                    );
                })
                .ToList();

            // Compute status
            string status = EventsService.ComputeStatus(ev);
            
            var evDto = new EventDto(
                ev.Id,
                ev.Title,
                ev.Description,
                ev.IsPublic,
                ev.StartTime,
                ev.EndTime,
                ev.Location,
                creatorSummary,
                ev.CreatedAt,
                attendeeSummaries,
                status,
                ev.Latitude,
                ev.Longitude
            );
            // Implementation for getting event by ID goes here
            return Ok(evDto);
        }
        
        // GET /api/events/mine
        [HttpGet("mine")]
        public async Task<ActionResult<List<EventDto>>> GetMyEvents()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized();

            // Load the creator once
            var creator = await _userManager.FindByIdAsync(userId);
            if (creator is null)
                return Unauthorized();

            var creatorSummary = new UserSummaryDto(
                creator.Id,
                creator.UserName!,
                creator.FirstName,
                creator.LastName
            );

            var myEvents = await _db.Events
                .Where(e => e.CreatedById == userId)
                .Include(e => e.Attendees)
                .OrderBy(e => e.StartTime)
                .ToListAsync();

            var result = new List<EventDto>();

            foreach (var ev in myEvents)
            {
                // attendee IDs for this event
                var attendeeIds = ev.Attendees
                    .Select(a => a.UserId)
                    .Distinct()
                    .ToList();

                var attendeeUsers = await _userManager.Users
                    .Where(u => attendeeIds.Contains(u.Id))
                    .ToListAsync();

                var usersById = attendeeUsers.ToDictionary(u => u.Id);

                var attendeeSummaries = ev.Attendees
                    .Where(a => usersById.ContainsKey(a.UserId))
                    .Select(a =>
                    {
                        var u = usersById[a.UserId];
                        return new UserSummaryDto(
                            u.Id,
                            u.UserName!,
                            u.FirstName,
                            u.LastName
                        );
                    })
                    .ToList();

                string status = EventsService.ComputeStatus(ev);

                result.Add(new EventDto(
                    ev.Id,
                    ev.Title,
                    ev.Description,
                    ev.IsPublic,
                    ev.StartTime,
                    ev.EndTime,
                    ev.Location,
                    creatorSummary,
                    ev.CreatedAt,
                    attendeeSummaries,
                    status,
                    ev.Latitude,
                    ev.Longitude
                ));
            }

            return Ok(result);
        }
        
        // GET /api/events/attending
        [HttpGet("attending")]
        public async Task<ActionResult<List<EventDto>>> GetEventsAttending()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            if (userId is null)
                return Unauthorized();
            
            var attendingEvents = await _db.Events
                .Where(e => e.Attendees.Any(a => a.UserId == userId))
                .Include(e => e.Attendees)
                .OrderBy(e => e.StartTime)
                .ToListAsync();
            
            var result = new List<EventDto>();
            foreach (var ev in attendingEvents)
            {
                var creator = await _userManager.FindByIdAsync(ev.CreatedById);
                if (creator is null) continue; // skip if creator not found

                var creatorSummary = new UserSummaryDto(
                    creator.Id,
                    creator.UserName!,
                    creator.FirstName,
                    creator.LastName
                );

                // attendee IDs for this event
                var attendeeIds = ev.Attendees
                    .Select(a => a.UserId)
                    .Distinct()
                    .ToList();

                var attendeeUsers = await _userManager.Users
                    .Where(u => attendeeIds.Contains(u.Id))
                    .ToListAsync();

                var usersById = attendeeUsers.ToDictionary(u => u.Id);

                var attendeeSummaries = ev.Attendees
                    .Where(a => usersById.ContainsKey(a.UserId))
                    .Select(a =>
                    {
                        var u = usersById[a.UserId];
                        return new UserSummaryDto(
                            u.Id,
                            u.UserName!,
                            u.FirstName,
                            u.LastName
                        );
                    })
                    .ToList();

                string status = EventsService.ComputeStatus(ev);

                result.Add(new EventDto(
                    ev.Id,
                    ev.Title,
                    ev.Description,
                    ev.IsPublic,
                    ev.StartTime,
                    ev.EndTime,
                    ev.Location,
                    creatorSummary,
                    ev.CreatedAt,
                    attendeeSummaries,
                    status,
                    ev.Latitude,
                    ev.Longitude
                ));
            }
            
            return Ok(result);
        }
        
        // POST /api/events/{id}/attend
        [HttpPost("{id:int}/attend")]
        public async Task<IActionResult> AttendEvent(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized();

            var ev = await _db.Events
                .Include(e => e.Attendees)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (ev == null)
                return NotFound("Event not found.");
            
            //check if its your event
            if (ev.CreatedById == userId)
                return BadRequest("You cannot attend your own event.");

            // Check if already attending
            var alreadyAttending = ev.Attendees
                .Any(a => a.UserId == userId);
            if (alreadyAttending)
                return BadRequest("You are already attending this event.");

            // Add attendee
            var attendee = new EventAttendee
            {
                EventId = ev.Id,
                UserId = userId,
                JoinedAt = DateTime.UtcNow
            };
            _db.EventAttendees.Add(attendee);
            await _db.SaveChangesAsync();

            return Ok("You are now attending the event.");
        }
        
        // POST /api/events/{id}/leave
        [HttpPost("{id:int}/leave")]
        public async Task<IActionResult> LeaveEvent(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized();

            var ev = await _db.Events
                .Include(e => e.Attendees)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (ev == null)
                return NotFound("Event not found.");

            // Check if attending
            var attendee = ev.Attendees
                .FirstOrDefault(a => a.UserId == userId);
            if (attendee == null)
                return BadRequest("You are not attending this event.");

            // Remove attendee
            _db.EventAttendees.Remove(attendee);
            await _db.SaveChangesAsync();

            return Ok("You have left the event.");
        }
        
        // DELETE /api/events/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteEvent(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized();

            var ev = await _db.Events
                .FirstOrDefaultAsync(e => e.Id == id);
            if (ev == null)
                return NotFound("Event not found.");

            // Only creator can delete
            if (ev.CreatedById != userId)
                return Forbid("You are not authorized to delete this event.");

            _db.Events.Remove(ev);
            await _db.SaveChangesAsync();

            return Ok("Event deleted successfully.");
        }
        
        // Patch /api/events/{id}
        [HttpPatch("{id:int}")]
        public async Task<IActionResult> UpdateEvent(int id, [FromBody] UpdateEventDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized();

            var ev = await _db.Events
                .FirstOrDefaultAsync(e => e.Id == id);
            if (ev == null)
                return NotFound("Event not found.");

            // Only creator can update
            if (ev.CreatedById != userId)
                return Forbid("You are not authorized to update this event.");

            // Update fields if provided
            if (!string.IsNullOrWhiteSpace(dto.Title))
                ev.Title = dto.Title.Trim();
            if (dto.Description != null)
                ev.Description = dto.Description.Trim();
            if (dto.IsPublic.HasValue)
                ev.IsPublic = dto.IsPublic.Value;
            if (dto.StartTime.HasValue)
                ev.StartTime = dto.StartTime.Value;
            if (dto.EndTime.HasValue)
                ev.EndTime = dto.EndTime.Value;
            if (!string.IsNullOrWhiteSpace(dto.Location))
                ev.Location = dto.Location.Trim();

            await _db.SaveChangesAsync();

            return Ok("Event updated successfully.");
        }
        
        // GET /api/events/public?from=2025-11-18T00:00:00Z&to=2025-11-20T00:00:00Z&lat=41.88&lng=-87.63&radiusKm=10
        [HttpGet("public")]
        [Authorize]
        public async Task<ActionResult<List<EventDto>>> GetPublicEvents(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] double? lat,
            [FromQuery] double? lng,
            [FromQuery] double? radiusKm
        )
        {
            var now = DateTime.UtcNow;

            // 1) Start with a plain IQueryable<Event>
            IQueryable<Event> query = _db.Events
                .Where(e => e.IsPublic && e.EndTime >= now);

            // 2) Optional date filters
            if (from.HasValue)
                query = query.Where(e => e.StartTime >= from.Value);

            if (to.HasValue)
                query = query.Where(e => e.StartTime <= to.Value);

            // 3) Optional location filter (simple bounding box)
            if (lat.HasValue && lng.HasValue && radiusKm.HasValue && radiusKm.Value > 0)
            {
                var centerLat = lat.Value;
                var centerLng = lng.Value;
                var radius = radiusKm.Value;

                // Rough conversion: 1 degree latitude ≈ 111.32 km
                var latDelta = radius / 111.32;

                // Longitude degrees shrink by cos(latitude)
                var lngDelta = radius / (111.32 * Math.Cos(centerLat * Math.PI / 180.0));

                var minLat = centerLat - latDelta;
                var maxLat = centerLat + latDelta;
                var minLng = centerLng - lngDelta;
                var maxLng = centerLng + lngDelta;

                query = query.Where(e =>
                    e.Latitude >= minLat && e.Latitude <= maxLat &&
                    e.Longitude >= minLng && e.Longitude <= maxLng);
            }

            // 4) Only now add Include
            query = query
                .Include(e => e.Attendees);

            var events = await query
                .OrderBy(e => e.StartTime)
                .ToListAsync();

            var result = new List<EventDto>();

            foreach (var ev in events)
            {
                // creator
                var creator = await _userManager.FindByIdAsync(ev.CreatedById);
                if (creator is null)
                    continue;

                var creatorSummary = new UserSummaryDto(
                    creator.Id,
                    creator.UserName!,
                    creator.FirstName,
                    creator.LastName
                );

                // attendees for this event
                var attendeeIds = ev.Attendees
                    .Select(a => a.UserId)
                    .Distinct()
                    .ToList();

                var attendeeUsers = await _userManager.Users
                    .Where(u => attendeeIds.Contains(u.Id))
                    .ToListAsync();

                var usersById = attendeeUsers.ToDictionary(u => u.Id);

                var attendeeSummaries = ev.Attendees
                    .Where(a => usersById.ContainsKey(a.UserId))
                    .Select(a =>
                    {
                        var u = usersById[a.UserId];
                        return new UserSummaryDto(
                            u.Id,
                            u.UserName!,
                            u.FirstName,
                            u.LastName
                        );
                    })
                    .ToList();

                var status = EventsService.ComputeStatus(ev);

                result.Add(new EventDto(
                    ev.Id,
                    ev.Title,
                    ev.Description,
                    ev.IsPublic,
                    ev.StartTime,
                    ev.EndTime,
                    ev.Location,
                    creatorSummary,
                    ev.CreatedAt,
                    attendeeSummaries,
                    status,
                    ev.Latitude,
                    ev.Longitude
                ));
            }

            return Ok(result);
        }

    }
}