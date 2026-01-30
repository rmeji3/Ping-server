
using Ping.Dtos.Common;
using Ping.Dtos.Events;
using Ping.Models.Events;

namespace Ping.Services.Events;

public interface IEventService
{
    Task<EventDto> CreateEventAsync(CreateEventDto dto, string userId);
    Task<EventDto> UpdateEventAsync(int id, UpdateEventDto dto, string userId);
    Task<EventDto?> GetEventByIdAsync(int id, string? userId = null);
    Task<PaginatedResult<EventDto>> GetMyEventsAsync(string userId, PaginationParams pagination);
    Task<PaginatedResult<EventDto>> GetEventsAttendingAsync(string userId, PaginationParams pagination);
    Task<PaginatedResult<EventDto>> GetPublicEventsAsync(EventFilterDto filter, PaginationParams pagination, string? userId = null);
    Task<PaginatedResult<EventDto>> GetEventsByPingAsync(int pingId, string? userId, PaginationParams pagination);
    Task<bool> DeleteEventAsync(int id, string userId);
    Task DeleteEventAsAdminAsync(int id);
    Task<bool> JoinEventAsync(int id, string userId);
    Task<bool> InviteUserAsync(int eventId, string inviterId, string targetUserId);
    Task<bool> UninviteUserAsync(int eventId, string requesterId, string targetUserId);
    Task<bool> RemoveAttendeeAsync(int eventId, string requesterId, string targetUserId);
    Task<bool> LeaveEventAsync(int id, string userId);
    Task<EventCommentDto> AddCommentAsync(int eventId, string userId, string content);
    Task<EventCommentDto> UpdateCommentAsync(int commentId, string userId, string content);
    Task<PaginatedResult<EventCommentDto>> GetCommentsAsync(int eventId, PaginationParams pagination);
    Task<bool> DeleteCommentAsync(int commentId, string userId);
    Task<PaginatedResult<FriendInviteDto>> GetFriendsToInviteAsync(int eventId, string userId, PaginationParams pagination, string? query = null);
}

