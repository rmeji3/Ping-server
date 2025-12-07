using Conquest.Dtos.Places;
using Conquest.Models.Places;

namespace Conquest.Services.Places;

public interface IPlaceService
{
    Task<PlaceDetailsDto> CreatePlaceAsync(UpsertPlaceDto dto, string userId);
    Task<PlaceDetailsDto?> GetPlaceByIdAsync(int id, string? userId);
    Task<IEnumerable<PlaceDetailsDto>> SearchNearbyAsync(double lat, double lng, double radiusKm, string? activityName, string? activityKind, PlaceVisibility? visibility, PlaceType? type, string? userId);
    Task<IEnumerable<PlaceDetailsDto>> GetFavoritedPlacesAsync(string userId);
    Task DeletePlaceAsync(int id, string userId);
    Task AddFavoriteAsync(int id, string userId);
    Task UnfavoriteAsync(int id, string userId);
}
