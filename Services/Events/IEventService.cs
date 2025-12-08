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
    Task<bool> DeleteEventAsync(int id, string userId);
    Task<bool> JoinEventAsync(int id, string userId);
    Task<bool> LeaveEventAsync(int id, string userId);
}
