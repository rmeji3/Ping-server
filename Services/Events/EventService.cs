using Conquest.Data.App;
using Conquest.Data.Auth;
using Conquest.Dtos.Common;
using Conquest.Dtos.Events;
using Conquest.Models.AppUsers;
using Conquest.Models.Events;
using Conquest.Services.Moderation;
using Conquest.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Events;

public class EventService(
    AppDbContext appDb,
    UserManager<AppUser> userManager,
    IModerationService moderationService,
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
            PlaceId = dto.PlaceId
        };
        
        if (dto.PlaceId.HasValue)
        {
            var place = await appDb.Places.FindAsync(dto.PlaceId.Value);
            if (place == null)
            {
                throw new ArgumentException($"Place with ID {dto.PlaceId} not found.");
            }
            // Enforce location data from Place
            ev.Location = place.Name; // Or place.Address? Using Name as "Place Name" seems appropriate for Event.Location
            ev.Latitude = place.Latitude;
            ev.Longitude = place.Longitude;
        }

        appDb.Events.Add(ev);
        await appDb.SaveChangesAsync();

        // Auto-join creator
        var attendance = new EventAttendee
        {
            EventId = ev.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        };
        appDb.EventAttendees.Add(attendance);
        await appDb.SaveChangesAsync();

        logger.LogInformation("Event created: {EventId} by {UserId}", ev.Id, userId);

        // Return DTO
        // For a fresh event, we know the creator is the only attendee
        var user = await userManager.FindByIdAsync(userId);
        var creatorSummary = new UserSummaryDto(user!.Id, user.UserName!, user.FirstName, user.LastName);
        
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
            new List<UserSummaryDto> { creatorSummary },
            "attending",
            ev.Latitude,
            ev.Longitude,
            ev.PlaceId
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

        // Place Linking & Unlinking Logic
        // Scenario 1: User selected a specific Place (Pin) -> Link it and overwrite location details
        if (dto.PlaceId.HasValue)
        {
            var place = await appDb.Places.FindAsync(dto.PlaceId.Value);
            if (place == null) throw new ArgumentException($"Place with ID {dto.PlaceId} not found");
            
            ev.PlaceId = dto.PlaceId.Value;
            ev.Location = place.Name;
            ev.Latitude = place.Latitude;
            ev.Longitude = place.Longitude;
        }
        // Scenario 2: User manually changed location (Location text OR Coords) but did NOT provide a PlaceId
        // This implies they are moving the pin or typing a custom address, so we must UNLINK the old Place.
        else if (dto.Location != null || dto.Latitude.HasValue || dto.Longitude.HasValue)
        {
            ev.PlaceId = null;
        }

        await appDb.SaveChangesAsync();
        
        logger.LogInformation("Event updated: {EventId} by {UserId}", ev.Id, userId);

        // Return updated DTO
        // Re-fetch or reuse? Reuse is fine but need creator user info
        var creatorUser = await userManager.FindByIdAsync(ev.CreatedById);
        var creatorSummary = creatorUser != null
            ? new UserSummaryDto(creatorUser.Id, creatorUser.UserName!, creatorUser.FirstName, creatorUser.LastName)
            : new UserSummaryDto("?", "Unknown", null, null);
            
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
            ? new UserSummaryDto(creatorUser.Id, creatorUser.UserName!, creatorUser.FirstName, creatorUser.LastName)
            : new UserSummaryDto("?", "Unknown", null, null);

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

    public async Task<PaginatedResult<EventDto>> GetPublicEventsAsync(double minLat, double maxLat, double minLng, double maxLng, PaginationParams pagination)
    {
        var query = appDb.Events
            .Include(e => e.Attendees)
            .Where(e => e.IsPublic)
            .Where(e => e.EndTime > DateTime.UtcNow)
            .Where(e => e.Latitude >= minLat && e.Latitude <= maxLat &&
                        e.Longitude >= minLng && e.Longitude <= maxLng)
            .OrderBy(e => e.StartTime);

        return await MapEventsBatchAsync(query, null, pagination);
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

    public async Task<bool> JoinEventAsync(int id, string userId)
    {
        var ev = await appDb.Events.FindAsync(id);
        if (ev is null) return false;

        var exists = await appDb.EventAttendees.AnyAsync(a => a.EventId == id && a.UserId == userId);
        if (exists) return true; // already joined

        appDb.EventAttendees.Add(new EventAttendee
        {
            EventId = id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
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
            var creatorSummary = new UserSummaryDto(creator.Id, creator.UserName!, creator.FirstName, creator.LastName);

            dtos.Add(EventMapper.MapToDto(ev, creatorSummary, usersById, currentUserId));
        }

        return new PaginatedResult<EventDto>(dtos, count, pagination.PageNumber, pagination.PageSize);
    }
}
