using Ping.Dtos.Activities;

namespace Ping.Services.Activities;

public interface IPingActivityService
{
    Task<PingActivityDetailsDto> CreatePingActivityAsync(CreatePingActivityDto dto, string userId);
    Task DeletePingActivityAsAdminAsync(int id);
    Task<global::Ping.Dtos.Common.PaginatedResult<PingActivityDetailsDto>> SearchActivitiesAsync(ActivitySearchDto searchDto);
}

