using Ping.Dtos.Common;
using Ping.Dtos.Pings;
using Ping.Models.Pings;

namespace Ping.Services.Pings;

public interface IPingService
{
    Task<PingDetailsDto> CreatePingAsync(UpsertPingDto dto, string userId);
    Task<PingDetailsDto?> GetPingByIdAsync(int id, string? userId);
    Task<PaginatedResult<PingDetailsDto>> SearchNearbyAsync(double? lat, double? lng, double? radiusKm, string? query, string[]? activityNames, string[]? pingGenreNames, string[]? tags, PingVisibility? visibility, PingType? type, string? userId, PaginationParams pagination);
    Task<PaginatedResult<PingDetailsDto>> GetFavoritedPingsAsync(string userId, PaginationParams pagination);
    Task<List<PingDetailsDto>> GetPingsByOwnerAsync(string userId, bool onlyClaimed = false);
    Task<PingDetailsDto> UpdatePingAsync(int id, UpdatePingDto dto, string userId);
    Task DeletePingAsync(int id, string userId);
    Task DeletePingAsAdminAsync(int id);
    Task AddFavoriteAsync(int id, string userId);
    Task UnfavoriteAsync(int id, string userId);
}

