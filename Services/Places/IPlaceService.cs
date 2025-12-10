using Conquest.Dtos.Common;
using Conquest.Dtos.Places;
using Conquest.Models.Places;

namespace Conquest.Services.Places;

public interface IPlaceService
{
    Task<PlaceDetailsDto> CreatePlaceAsync(UpsertPlaceDto dto, string userId);
    Task<PlaceDetailsDto?> GetPlaceByIdAsync(int id, string? userId);
    Task<PaginatedResult<PlaceDetailsDto>> SearchNearbyAsync(double lat, double lng, double radiusKm, string? activityName, string? activityKind, PlaceVisibility? visibility, PlaceType? type, string? userId, PaginationParams pagination);
    Task<PaginatedResult<PlaceDetailsDto>> GetFavoritedPlacesAsync(string userId, PaginationParams pagination);
    Task<List<PlaceDetailsDto>> GetPlacesByOwnerAsync(string userId, bool onlyClaimed = false);
    Task<PlaceDetailsDto> UpdatePlaceAsync(int id, UpsertPlaceDto dto, string userId);
    Task DeletePlaceAsync(int id, string userId);
    Task DeletePlaceAsAdminAsync(int id);
    Task AddFavoriteAsync(int id, string userId);
    Task UnfavoriteAsync(int id, string userId);
}
