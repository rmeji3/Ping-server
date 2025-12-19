using Ping.Data.App;
using Ping.Data.Auth;
using Ping.Dtos.Common;
using Ping.Dtos.Events;
using Ping.Models.AppUsers;
using Ping.Models.Events;
using Ping.Services.Moderation;
using Ping.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Events;

public class EventService(
    AppDbContext appDb,
    UserManager<AppUser> userManager,
    IModerationService moderationService,
    Services.Blocks.IBlockService blockService,
    ILogger<EventService> logger) : IEventService
{
    public async Task<EventDto> CreateEventAsync(CreateEventDto dto, string userId)
    {
        // 1. Moderate content
        var titleCheck = await moderationService.CheckContentAsync(dto.Title);
        if (titleCheck.IsFlagged)
        {
            throw new ArgumentException($"Title violates content policy: {titleCheck.Reason}");
        }

        if (!string.IsNullOrWhiteSpace(dto.Description))
        {
            var descCheck = await moderationService.CheckContentAsync(dto.Description);
            if (descCheck.IsFlagged)
            {
                throw new ArgumentException($"Description violates content policy: {descCheck.Reason}");
            }
        }

        if (!string.IsNullOrWhiteSpace(dto.Location))
        {
            var locCheck = await moderationService.CheckContentAsync(dto.Location);
            if (locCheck.IsFlagged)
            {
                throw new ArgumentException($"Location violates content policy: {locCheck.Reason}");
            }
        }

        var ev = new Event
        {
            Title = dto.Title,
            Description = dto.Description,
            IsPublic = dto.IsPublic,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Location = dto.Location,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
            PingId = dto.PingId,
            EventGenreId = dto.EventGenreId
        };
        
        if (dto.PingId.HasValue)
        {
            var ping = await appDb.Pings.FindAsync(dto.PingId.Value);
            if (ping == null)
            {
                throw new ArgumentException($"Ping with ID {dto.PingId} not found.");
            }
            // Enforce location data from Ping
            ev.Location = ping.Name; // Or ping.Address? Using Name as "Ping Name" seems appropriate for Event.Location
            ev.Latitude = ping.Latitude;
            ev.Longitude = ping.Longitude;
        }

        appDb.Events.Add(ev);
        await appDb.SaveChangesAsync();

        // Auto-join creator
        var attendance = new EventAttendee
        {
            EventId = ev.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
            Status = AttendeeStatus.Attending
        };
        appDb.EventAttendees.Add(attendance);
        await appDb.SaveChangesAsync();

        logger.LogInformation("Event created: {EventId} by {UserId}", ev.Id, userId);

        // Return DTO
        // For a fresh event, we know the creator is the only attendee
        var user = await userManager.FindByIdAsync(userId);
        var creatorSummary = new UserSummaryDto(user!.Id, user.UserName!, user.FirstName, user.LastName, user.ProfileImageUrl);
        
        return new EventDto(
            ev.Id,
            ev.Title,
            ev.Description,
            ev.IsPublic,
            ev.StartTime,
            ev.EndTime,
            ev.Location,
            creatorSummary,
            ev.CreatedAt,
            new List<EventAttendeeDto> 
            { 
                new EventAttendeeDto(creatorSummary.Id, creatorSummary.UserName, creatorSummary.FirstName, creatorSummary.LastName, creatorSummary.ProfilePictureUrl, "attending") 
            },
            "attending",
            ev.Latitude,
            ev.Longitude,
            ev.PingId,
            ev.EventGenreId,
            ev.EventGenre?.Name
        );
    }

    public async Task<EventDto> UpdateEventAsync(int id, UpdateEventDto dto, string userId)
    {
        var ev = await appDb.Events
            .Include(e => e.Attendees)
            .FirstOrDefaultAsync(e => e.Id == id);
            
        if (ev == null)
        {
            throw new KeyNotFoundException("Event not found");
        }
        
        if (ev.CreatedById != userId)
        {
            throw new UnauthorizedAccessException("Only the creator can update this event");
        }

        // Apply string updates with moderation
        if (dto.Title != null)
        {
            var check = await moderationService.CheckContentAsync(dto.Title);
            if (check.IsFlagged) throw new ArgumentException($"Title violates content policy: {check.Reason}");
            ev.Title = dto.Title;
        }

        if (dto.Description != null)
        {
            var check = await moderationService.CheckContentAsync(dto.Description);
            if (check.IsFlagged) throw new ArgumentException($"Description violates content policy: {check.Reason}");
            ev.Description = dto.Description;
        }

        if (dto.Location != null)
        {
            var check = await moderationService.CheckContentAsync(dto.Location);
            if (check.IsFlagged) throw new ArgumentException($"Location violates content policy: {check.Reason}");
            ev.Location = dto.Location;
        }
        
        // Apply simple fields
        if (dto.IsPublic.HasValue) ev.IsPublic = dto.IsPublic.Value;
        
        bool timeChanged = false;
        if (dto.StartTime.HasValue)
        {
            ev.StartTime = dto.StartTime.Value;
            timeChanged = true;
        }
        if (dto.EndTime.HasValue)
        {
            ev.EndTime = dto.EndTime.Value;
            timeChanged = true;
        }
        
        if (timeChanged)
        {
             if (ev.StartTime < DateTime.UtcNow)
                throw new ArgumentException("Event start time must be in the future.");
             if (ev.EndTime <= ev.StartTime)
                throw new ArgumentException("End time must be after start time.");
             if ((ev.EndTime - ev.StartTime).TotalMinutes < 15)
                throw new ArgumentException("Event duration must be at least 15 minutes.");
        }

        if (dto.Latitude.HasValue) ev.Latitude = dto.Latitude.Value;
        if (dto.Longitude.HasValue) ev.Longitude = dto.Longitude.Value;
        if (dto.EventGenreId.HasValue) ev.EventGenreId = dto.EventGenreId.Value;

        // Ping Linking & Unlinking Logic
        // Scenario 1: User selected a specific Ping (Pin) -> Link it and overwrite location details
        if (dto.PingId.HasValue)
        {
            var ping = await appDb.Pings.FindAsync(dto.PingId.Value);
            if (ping == null) throw new ArgumentException($"Ping with ID {dto.PingId} not found");
            
            ev.PingId = dto.PingId.Value;
            ev.Location = ping.Name;
            ev.Latitude = ping.Latitude;
            ev.Longitude = ping.Longitude;
        }
        // Scenario 2: User manually changed location (Location text OR Coords) but did NOT provide a PingId
        // This implies they are moving the pin or typing a custom address, so we must UNLINK the old Ping.
        else if (dto.Location != null || dto.Latitude.HasValue || dto.Longitude.HasValue)
        {
            ev.PingId = null;
        }

        await appDb.SaveChangesAsync();
        
        logger.LogInformation("Event updated: {EventId} by {UserId}", ev.Id, userId);

        // Return updated DTO
        // Re-fetch or reuse? Reuse is fine but need creator user info
        var creatorUser = await userManager.FindByIdAsync(ev.CreatedById);
        var creatorSummary = creatorUser != null
            ? new UserSummaryDto(creatorUser.Id, creatorUser.UserName!, creatorUser.FirstName, creatorUser.LastName, creatorUser.ProfileImageUrl)
            : new UserSummaryDto("?", "Unknown", null, null, null);
            
        // Get attendees
        var attendeeIds = ev.Attendees.Select(a => a.UserId).Distinct().ToList();
        var attendeeUsers = await userManager.Users.Where(u => attendeeIds.Contains(u.Id)).ToListAsync();
        var usersById = attendeeUsers.ToDictionary(u => u.Id);
        
        return EventMapper.MapToDto(ev, creatorSummary, usersById, userId); 
    }

    public async Task<EventDto?> GetEventByIdAsync(int id, string? userId = null)
    {
        var ev = await appDb.Events
            .Include(e => e.Attendees)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev is null) return null;

        // Load users for creator + attendees
        var creatorUser = await userManager.FindByIdAsync(ev.CreatedById);
        var creatorSummary = creatorUser != null
            ? new UserSummaryDto(creatorUser.Id, creatorUser.UserName!, creatorUser.FirstName, creatorUser.LastName, creatorUser.ProfileImageUrl)
            : new UserSummaryDto("?", "Unknown", null, null, null);

        var attendeeIds = ev.Attendees.Select(a => a.UserId).Distinct().ToList();
        var attendeeUsers = await userManager.Users
            .Where(u => attendeeIds.Contains(u.Id))
            .ToListAsync();
        
        var usersById = attendeeUsers.ToDictionary(u => u.Id);

        return EventMapper.MapToDto(ev, creatorSummary, usersById, userId); 
    }
    
    public async Task<PaginatedResult<EventDto>> GetMyEventsAsync(string userId, PaginationParams pagination)
    {
        var query = appDb.Events
            .Include(e => e.Attendees)
            .Where(e => e.CreatedById == userId)
            .OrderByDescending(e => e.StartTime);

        return await MapEventsBatchAsync(query, userId, pagination);
    }

    public async Task<PaginatedResult<EventDto>> GetEventsAttendingAsync(string userId, PaginationParams pagination)
    {
        var eventIdsQuery = appDb.EventAttendees
            .Where(a => a.UserId == userId)
            .Select(a => a.EventId);

        var query = appDb.Events
            .Include(e => e.Attendees)
            .Where(e => eventIdsQuery.Contains(e.Id))
            .OrderByDescending(e => e.StartTime);

        return await MapEventsBatchAsync(query, userId, pagination);
    }

    public async Task<bool> InviteUserAsync(int eventId, string inviterId, string targetUserId)
    {
        var ev = await appDb.Events.FindAsync(eventId);
        if (ev == null) throw new KeyNotFoundException("Event not found");

        // Permission Check
        if (!ev.IsPublic)
        {
            // Private: Only creator can invite
            if (ev.CreatedById != inviterId) 
            {
                throw new UnauthorizedAccessException("Only the creator can invite to private events.");
            }
        }
        // Public: Anyone can invite (no check needed)

        // Check if target exists
        var targetUser = await userManager.FindByIdAsync(targetUserId);
        if (targetUser == null) throw new KeyNotFoundException("Target user not found");

        // Check if already attending/invited
        var existing = await appDb.EventAttendees.FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == targetUserId);
        if (existing != null)
        {
            // Already there
            return true; 
        }

        appDb.EventAttendees.Add(new EventAttendee
        {
            EventId = eventId,
            UserId = targetUserId,
            JoinedAt = DateTime.UtcNow,
            Status = AttendeeStatus.Invited
        });

        await appDb.SaveChangesAsync();
        await appDb.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UninviteUserAsync(int eventId, string requesterId, string targetUserId)
    {
        var ev = await appDb.Events.FindAsync(eventId);
        if (ev == null) throw new KeyNotFoundException("Event not found");

        if (ev.CreatedById != requesterId) throw new UnauthorizedAccessException("Only creator can uninvite users.");

        var att = await appDb.EventAttendees.FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == targetUserId);
        if (att == null) return false; // Not attending/invited

        appDb.EventAttendees.Remove(att);
        await appDb.SaveChangesAsync();
        return true;
    }

    public async Task<PaginatedResult<EventDto>> GetEventsByPingAsync(int pingId, string? userId, PaginationParams pagination)
    {
        var query = appDb.Events
            .Include(e => e.Attendees)
            .Where(e => e.PingId == pingId)
            .Where(e => e.EndTime > DateTime.UtcNow)
            .Where(e => e.IsPublic || 
                        (userId != null && (e.CreatedById == userId || e.Attendees.Any(a => a.UserId == userId))))
            .OrderBy(e => e.StartTime)
            .AsQueryable();

        // Filter Blacklisted Users
        if (userId != null)
        {
            var blacklistedIds = await blockService.GetBlacklistedUserIdsAsync(userId);
            if (blacklistedIds.Count > 0)
            {
                query = query.Where(e => !blacklistedIds.Contains(e.CreatedById));
            }
        }

        return await MapEventsBatchAsync(query, userId, pagination);
    }

    public async Task<PaginatedResult<EventDto>> GetPublicEventsAsync(double minLat, double maxLat, double minLng, double maxLng, PaginationParams pagination, string? userId = null)
    {
        var query = appDb.Events
            .Include(e => e.Attendees)
            .Where(e => e.IsPublic)
            .Where(e => e.EndTime > DateTime.UtcNow)
            .Where(e => e.Latitude >= minLat && e.Latitude <= maxLat &&
                        e.Longitude >= minLng && e.Longitude <= maxLng)
            .OrderBy(e => e.StartTime)
            .AsQueryable();

        // Filter Blacklisted Users
        if (userId != null)
        {
            var blacklistedIds = await blockService.GetBlacklistedUserIdsAsync(userId);
            if (blacklistedIds.Count > 0)
            {
                // Exclude events created by blacklisted users
                query = query.Where(e => !blacklistedIds.Contains(e.CreatedById));
            }
        }

        return await MapEventsBatchAsync(query, userId, pagination);
    }

    public async Task<bool> DeleteEventAsync(int id, string userId)
    {
        var ev = await appDb.Events.FindAsync(id);
        if (ev is null) return false;
        if (ev.CreatedById != userId) throw new UnauthorizedAccessException("Not owner");

        appDb.Events.Remove(ev);
        await appDb.SaveChangesAsync();
        logger.LogInformation("Event deleted: {EventId} by {UserId}", id, userId);
        return true;
    }

    public async Task DeleteEventAsAdminAsync(int id)
    {
        var ev = await appDb.Events.FindAsync(id);
        if (ev != null)
        {
            appDb.Events.Remove(ev);
            await appDb.SaveChangesAsync();
            logger.LogInformation("Event deleted by Admin: {EventId}", id);
        }
    }

    public async Task<bool> JoinEventAsync(int id, string userId)
    {
        var ev = await appDb.Events.FindAsync(id);
        if (ev is null) return false;

        var attendance = await appDb.EventAttendees.FirstOrDefaultAsync(a => a.EventId == id && a.UserId == userId);
        if (attendance != null)
        {
            if (attendance.Status == AttendeeStatus.Invited)
            {
                // Accept invite
                attendance.Status = AttendeeStatus.Attending;
                attendance.JoinedAt = DateTime.UtcNow; // Update join time? Or keep original? Let's update.
                await appDb.SaveChangesAsync();
                return true;
            }
            return true; // already joined/attending
        }

        appDb.EventAttendees.Add(new EventAttendee
        {
            EventId = id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
            Status = AttendeeStatus.Attending
        });
        await appDb.SaveChangesAsync();
        return true;
    }

    public async Task<bool> LeaveEventAsync(int id, string userId)
    {
        var att = await appDb.EventAttendees
            .FirstOrDefaultAsync(a => a.EventId == id && a.UserId == userId);
        
        if (att is null) return false; // not attending

        appDb.EventAttendees.Remove(att);
        await appDb.SaveChangesAsync();
        return true;
    }

    private async Task<PaginatedResult<EventDto>> MapEventsBatchAsync(IQueryable<Event> query, string? currentUserId, PaginationParams pagination)
    {
        // 1. Get Count and Page Items
        var count = await query.CountAsync();
        var events = await query
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        if (!events.Any()) return new PaginatedResult<EventDto>(new List<EventDto>(), count, pagination.PageNumber, pagination.PageSize);

        // 2. Collect all user IDs (creators + attendees)
        var userIds = new HashSet<string>();
        foreach (var e in events)
        {
            userIds.Add(e.CreatedById);
            foreach (var a in e.Attendees) userIds.Add(a.UserId);
        }

        // 3. Fetch all users in one query
        var users = await userManager.Users
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync();
        
        var usersById = users.ToDictionary(u => u.Id);

        // 4. Map
        var dtos = new List<EventDto>();
        foreach (var ev in events)
        {
            if (!usersById.TryGetValue(ev.CreatedById, out var creator))
            {
                creator = new AppUser { Id = ev.CreatedById, UserName = "Unknown", FirstName = "Unknown", LastName = "User" };
            }
            var creatorSummary = new UserSummaryDto(creator.Id, creator.UserName!, creator.FirstName, creator.LastName, creator.ProfileImageUrl);

            dtos.Add(EventMapper.MapToDto(ev, creatorSummary, usersById, currentUserId));
        }

        return new PaginatedResult<EventDto>(dtos, count, pagination.PageNumber, pagination.PageSize);
    }
}

