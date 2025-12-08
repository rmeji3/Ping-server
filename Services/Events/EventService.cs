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

    public async Task<EventDto?> GetEventByIdAsync(int id)
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

        return EventMapper.MapToDto(ev, creatorSummary, usersById, null); // status is calculated in controller if needed, or we pass null here and let caller handle logic? 
        // Actually, status depends on "me". 
        // Service shouldn't know about "me" for a generic GetById unless we pass it.
        // Let's keep it simple and return the DTO. The controller can override status if needed, 
        // but wait, the DTO has a Status field.
        // We should probably pass "currentUserId" to this method if we want to fill status correctly.
        // But the interface signature I defined doesn't have it. 
        // Let's stick to returning the DTO with "unknown" status and let controller fill it?
        // Or better, update interface to take optional userId.
        // For now, I'll return "unknown" status.
    }

    // Overload or update interface? Let's update interface in next step if needed. 
    // Actually, looking at my interface: GetEventByIdAsync(int id).
    // I'll return "unknown" for status.
    
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
