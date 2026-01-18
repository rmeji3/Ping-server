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
    AuthDbContext authDb,
    UserManager<AppUser> userManager,
    IModerationService moderationService,
    Services.Blocks.IBlockService blockService,
    Services.Follows.IFollowService followService,
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

        var ping = await appDb.Pings.FindAsync(dto.PingId);
        if (ping == null)
        {
            throw new ArgumentException($"Ping with ID {dto.PingId} not found.");
        }

        // --- Privacy Constraints ---
        if (ping.Visibility == Models.Pings.PingVisibility.Private)
        {
            throw new ArgumentException("Places marked as 'Private' cannot host events.");
        }

        if (ping.Visibility == Models.Pings.PingVisibility.Friends && dto.IsPublic)
        {
            throw new ArgumentException("Events hosted at 'Friends Only' places must be private (invite-only).");
        }
        // --- End Privacy Constraints ---

        var ev = new Event
        {
            Title = dto.Title,
            Description = dto.Description,
            IsPublic = dto.IsPublic,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
            PingId = dto.PingId,
            EventGenreId = dto.EventGenreId,
            ImageUrl = dto.ImageUrl,
            ThumbnailUrl = dto.ThumbnailUrl,
            Price = dto.Price
        };

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
        var creatorSummary = new UserSummaryDto(user!.Id, user.UserName!, user.ProfileImageUrl);
        
        return new EventDto(
            ev.Id,
            ev.Title,
            ev.Description,
            ev.IsPublic,
            ev.StartTime,
            ev.EndTime,
            ping.Name,
            creatorSummary,
            ev.CreatedAt,
            new List<EventAttendeeDto> 
            { 
                new EventAttendeeDto(creatorSummary.Id, creatorSummary.UserName, creatorSummary.ProfilePictureUrl, "attending") 
            },
            "attending",
            ping.Latitude,
            ping.Longitude,
            ev.PingId,
            ev.EventGenreId,
            ev.EventGenre?.Name,
            ev.ImageUrl ?? null,
            ev.ThumbnailUrl ?? null,
            ev.Price ?? null,
            true, // IsHosting (creator)
            true, // IsAttending (creator auto-joins)
            new List<string>(), // FriendThumbnails (no other attendees)
            ping.Address
        );
    }

    public async Task<EventDto> UpdateEventAsync(int id, UpdateEventDto dto, string userId)
    {
        var ev = await appDb.Events
            .Include(e => e.Attendees)
            .Include(e => e.Ping)
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

        if (dto.EventGenreId.HasValue) ev.EventGenreId = dto.EventGenreId.Value;
        
        // Apply new optional fields
        if (dto.ImageUrl != null) ev.ImageUrl = dto.ImageUrl;
        if (dto.ThumbnailUrl != null) ev.ThumbnailUrl = dto.ThumbnailUrl;
        if (dto.Price.HasValue) ev.Price = dto.Price.Value;

        if (dto.PingId.HasValue)
        {
            var ping = await appDb.Pings.FindAsync(dto.PingId.Value);
            if (ping == null) throw new KeyNotFoundException("New Ping location not found");
            
            ev.PingId = dto.PingId.Value;
            ev.Ping = ping; // Update the navigation property for the check below
        }

        // Re-validate privacy constraints if Ping or Visibility changed
        if (ev.Ping.Visibility == Models.Pings.PingVisibility.Private)
        {
            throw new ArgumentException("Places marked as 'Private' cannot host events.");
        }

        if (ev.Ping.Visibility == Models.Pings.PingVisibility.Friends && ev.IsPublic)
        {
            throw new ArgumentException("Events hosted at 'Friends Only' places must be private (invite-only).");
        }

        await appDb.SaveChangesAsync();
        
        logger.LogInformation("Event updated: {EventId} by {UserId}", ev.Id, userId);

        // Return updated DTO
        var creatorUser = await userManager.FindByIdAsync(ev.CreatedById);
        var creatorSummary = creatorUser != null
            ? new UserSummaryDto(creatorUser.Id, creatorUser.UserName!, creatorUser.ProfileImageUrl)
            : new UserSummaryDto("?", "Unknown", null);
            
        // Get attendees
        var attendeeIds = ev.Attendees.Select(a => a.UserId).Distinct().ToList();
        var attendeeUsers = await userManager.Users.Where(u => attendeeIds.Contains(u.Id)).ToListAsync();
        var usersById = attendeeUsers.ToDictionary(u => u.Id);
        
        var friendIds = await followService.GetMutualIdsAsync(userId);

        return EventMapper.MapToDto(ev, creatorSummary, usersById, userId, friendIds); 
    }

    public async Task<EventDto?> GetEventByIdAsync(int id, string? userId = null)
    {
        var ev = await appDb.Events
            .Include(e => e.Attendees)
            .Include(e => e.Ping)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev is null) return null;

        // Load users for creator + attendees
        var creatorUser = await userManager.FindByIdAsync(ev.CreatedById);
        var creatorSummary = creatorUser != null
            ? new UserSummaryDto(creatorUser.Id, creatorUser.UserName!, creatorUser.ProfileImageUrl)
            : new UserSummaryDto("?", "Unknown", null);

        var attendeeIds = ev.Attendees.Select(a => a.UserId).Distinct().ToList();
        var attendeeUsers = await userManager.Users
            .Where(u => attendeeIds.Contains(u.Id))
            .ToListAsync();
        
        var usersById = attendeeUsers.ToDictionary(u => u.Id);

        IReadOnlyList<string>? friendIds = null;
        if (userId != null)
        {
            friendIds = await followService.GetMutualIdsAsync(userId);
        }

        return EventMapper.MapToDto(ev, creatorSummary, usersById, userId, friendIds); 
    }
    
    public async Task<PaginatedResult<EventDto>> GetMyEventsAsync(string userId, PaginationParams pagination)
    {
        var query = appDb.Events
            .Include(e => e.Attendees)
            .Include(e => e.Ping)
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
            .Include(e => e.Ping)
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
            .Include(e => e.Ping)
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

    public async Task<PaginatedResult<EventDto>> GetPublicEventsAsync(EventFilterDto filter, PaginationParams pagination, string? userId = null)
    {
        var query = appDb.Events
            .Include(e => e.Attendees)
            .Include(e => e.Ping)
            .Where(e => e.IsPublic)
            .Where(e => e.EndTime > DateTime.UtcNow)
            .AsQueryable();

        // Keyword Search
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var search = filter.Query.Trim().ToLowerInvariant();
            query = query.Where(e => e.Title.ToLower().Contains(search) || (e.Description != null && e.Description.ToLower().Contains(search)));
        }

        // Geospatial: Required
        if (filter.Latitude.HasValue && filter.Longitude.HasValue && filter.RadiusKm.HasValue)
        {
            var minLat = filter.Latitude.Value - (filter.RadiusKm.Value / 111.32);
            var maxLat = filter.Latitude.Value + (filter.RadiusKm.Value / 111.32);
            var lngDelta = filter.RadiusKm.Value / (111.32 * Math.Cos(filter.Latitude.Value * Math.PI / 180.0));
            var minLng = filter.Longitude.Value - lngDelta;
            var maxLng = filter.Longitude.Value + lngDelta;

            query = query.Where(e => e.Ping.Location.Y >= minLat && e.Ping.Location.Y <= maxLat &&
                                     e.Ping.Location.X >= minLng && e.Ping.Location.X <= maxLng);
        }

        // Price Filter
        if (filter.MinPrice.HasValue)
        {
            query = query.Where(e => e.Price >= filter.MinPrice.Value);
        }
        if (filter.MaxPrice.HasValue)
        {
            query = query.Where(e => e.Price <= filter.MaxPrice.Value);
        }

        // Date Range
        if (filter.FromDate.HasValue)
        {
            query = query.Where(e => e.StartTime >= filter.FromDate.Value);
        }
        if (filter.ToDate.HasValue)
        {
            query = query.Where(e => e.StartTime <= filter.ToDate.Value);
        }

        // Genre
        if (filter.GenreId.HasValue)
        {
            query = query.Where(e => e.EventGenreId == filter.GenreId.Value);
        }

        query = query.OrderBy(e => e.StartTime);

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
            .Include(e => e.Ping)
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

        IReadOnlyList<string>? friendIds = null;
        if (currentUserId != null)
        {
            friendIds = await followService.GetMutualIdsAsync(currentUserId);
        }

        // 4. Map
        var dtos = new List<EventDto>();
        foreach (var ev in events)
        {
            if (!usersById.TryGetValue(ev.CreatedById, out var creator))
            {
                creator = new AppUser { Id = ev.CreatedById, UserName = "Unknown", FirstName = "Unknown", LastName = "User" };
            }
            var creatorSummary = new UserSummaryDto(creator.Id, creator.UserName!, creator.ProfileImageUrl);

            dtos.Add(EventMapper.MapToDto(ev, creatorSummary, usersById, currentUserId, friendIds));
        }

        return new PaginatedResult<EventDto>(dtos, count, pagination.PageNumber, pagination.PageSize);
    }

    public async Task<EventCommentDto> AddCommentAsync(int eventId, string userId, string content)
    {
        // Validation: Max 100 words (word count approx)
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Comment cannot be empty.");
        
        var wordCount = content.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 100)
        {
            throw new ArgumentException($"Comment exceeds 100 words limit (Current: {wordCount}).");
        }

        var ev = await appDb.Events.FindAsync(eventId);
        if (ev == null) throw new KeyNotFoundException("Event not found.");

        // Check moderation
        var check = await moderationService.CheckContentAsync(content);
        if (check.IsFlagged) throw new ArgumentException($"Comment violates content policy: {check.Reason}");

        var comment = new EventComment
        {
            EventId = eventId,
            UserId = userId,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };

        appDb.EventComments.Add(comment);
        await appDb.SaveChangesAsync();

        var user = await userManager.FindByIdAsync(userId);

        return new EventCommentDto(
            comment.Id,
            comment.Content,
            comment.CreatedAt,
            userId,
            user?.UserName ?? "Unknown",
            user?.ProfileImageUrl,
            user?.ProfileThumbnailUrl
        );
    }

    public async Task<PaginatedResult<EventCommentDto>> GetCommentsAsync(int eventId, PaginationParams pagination)
    {
        var query = appDb.EventComments
            .AsNoTracking()
            .Where(c => c.EventId == eventId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new EventCommentDto(
                c.Id,
                c.Content,
                c.CreatedAt,
                c.UserId,
                c.User!.UserName!,
                c.User.ProfileImageUrl,
                c.User.ProfileThumbnailUrl
            ));
        
        return await query.ToPaginatedResultAsync(pagination);
    }

    public async Task<EventCommentDto> UpdateCommentAsync(int commentId, string userId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Comment cannot be empty.");
        
        var wordCount = content.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 100)
        {
            throw new ArgumentException($"Comment exceeds 100 words limit (Current: {wordCount}).");
        }

        var comment = await appDb.EventComments.FindAsync(commentId);
        if (comment == null) throw new KeyNotFoundException("Comment not found.");

        if (comment.UserId != userId)
        {
            throw new UnauthorizedAccessException("Not comment owner.");
        }

        var check = await moderationService.CheckContentAsync(content);
        if (check.IsFlagged) throw new ArgumentException($"Comment violates content policy: {check.Reason}");

        comment.Content = content;
        
        await appDb.SaveChangesAsync();

        var user = await userManager.FindByIdAsync(userId);
        return new EventCommentDto(
            comment.Id,
            comment.Content,
            comment.CreatedAt,
            userId,
            user?.UserName ?? "Unknown",
            user?.ProfileImageUrl,
            user?.ProfileThumbnailUrl
        );
    }

    public async Task<bool> DeleteCommentAsync(int commentId, string userId)
    {
        var comment = await appDb.EventComments.FindAsync(commentId);
        if (comment == null) return false;

        // Allow deletion if owner or admin (admin check logic might be higher up, but for service method usually owner)
        // Also Event Owner could potentially delete? For now, only Comment Owner.
        if (comment.UserId != userId)
        {
            throw new UnauthorizedAccessException("Not comment owner.");
        }

        appDb.EventComments.Remove(comment);
        await appDb.SaveChangesAsync();
        return true;
    }

    public async Task<PaginatedResult<FriendInviteDto>> GetFriendsToInviteAsync(int eventId, string userId, PaginationParams pagination)
    {
        // Mutuals: Users I follow who also follow me
        var friendsQuery = authDb.Follows
            .Where(f => f.FollowerId == userId && 
                        authDb.Follows.Any(f2 => f2.FollowerId == f.FolloweeId && f2.FolloweeId == userId))
            .Select(f => f.Followee);

        var totalCount = await friendsQuery.CountAsync();
        
        var friends = await friendsQuery
            .OrderBy(u => u.UserName)
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new { u.Id, u.UserName, u.FirstName, u.LastName, u.ProfileImageUrl })
            .ToListAsync();

        if (!friends.Any())
        {
            return new PaginatedResult<FriendInviteDto>(new List<FriendInviteDto>(), totalCount, pagination.PageNumber, pagination.PageSize);
        }

        var friendIds = friends.Select(f => f.Id).ToList();

        var attendees = await appDb.EventAttendees
            .Where(a => a.EventId == eventId && friendIds.Contains(a.UserId))
            .Select(a => new { a.UserId, a.Status })
            .ToListAsync();
            
        var attendeeMap = attendees.ToDictionary(a => a.UserId, a => a.Status);

        var dtos = friends.Select(f => 
        {
            string status = "None";
            if (attendeeMap.TryGetValue(f.Id, out var attStatus))
            {
                status = attStatus.ToString(); 
            }
            
            return new FriendInviteDto(f.Id, f.UserName!, f.ProfileImageUrl, status);
        }).ToList();

        return new PaginatedResult<FriendInviteDto>(dtos, totalCount, pagination.PageNumber, pagination.PageSize);
    }
}

