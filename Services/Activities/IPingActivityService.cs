using Ping.Dtos.Activities;

namespace Ping.Services.Activities;

public interface IPingActivityService
{
    Task<PingActivityDetailsDto> CreatePingActivityAsync(CreatePingActivityDto dto);
    Task DeletePingActivityAsAdminAsync(int id);
}

