using System.Collections.Generic;

namespace Ping.Services.Google;

public record GooglePingInfo(string Name, string? Address, double? Lat, double? Lng, IReadOnlyList<string>? Types = null);

public interface IPingNameService
{
    Task<string?> GetPingNameAsync(double lat, double lng);
    Task<GooglePingInfo?> GetGooglePlaceByIdAsync(string placeId);
    Task<List<GooglePingInfo>> SearchPingsAsync(string query, double lat, double lng, double radiusKm);

    /// <summary>
    /// Returns the raw Google Place types array for a given place ID (e.g. ["restaurant", "food", "establishment"]).
    /// Used by the genre auto-classifier (Tier 1). Returns an empty list on failure.
    /// </summary>
    Task<IReadOnlyList<string>> GetGooglePlaceTypesAsync(string placeId);
}



