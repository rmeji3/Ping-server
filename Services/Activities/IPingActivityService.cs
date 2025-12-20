using Ping.Dtos.Activities;

namespace Ping.Services.Activities;

public interface IPingActivityService
{
    Task<PingActivityDetailsDto> CreatePingActivityAsync(CreatePingActivityDto dto, string userId);
    Task DeletePingActivityAsAdminAsync(int id);
}

