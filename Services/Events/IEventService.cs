
using Conquest.Dtos.Common;
using Conquest.Dtos.Events;
using Conquest.Models.Events;

namespace Conquest.Services.Events;

public interface IEventService
{
    Task<EventDto> CreateEventAsync(CreateEventDto dto, string userId);
    Task<EventDto> UpdateEventAsync(int id, UpdateEventDto dto, string userId);
    Task<EventDto?> GetEventByIdAsync(int id, string? userId = null);
    Task<PaginatedResult<EventDto>> GetMyEventsAsync(string userId, PaginationParams pagination);
    Task<PaginatedResult<EventDto>> GetEventsAttendingAsync(string userId, PaginationParams pagination);
    Task<PaginatedResult<EventDto>> GetPublicEventsAsync(double minLat, double maxLat, double minLng, double maxLng, PaginationParams pagination);
    Task<PaginatedResult<EventDto>> GetEventsByPlaceAsync(int placeId, string? userId, PaginationParams pagination);
    Task<bool> DeleteEventAsync(int id, string userId);
    Task<bool> JoinEventAsync(int id, string userId);
    Task<bool> InviteUserAsync(int eventId, string inviterId, string targetUserId);
    Task<bool> UninviteUserAsync(int eventId, string requesterId, string targetUserId);
    Task<bool> LeaveEventAsync(int id, string userId);
}
