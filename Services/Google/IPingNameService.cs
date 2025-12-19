namespace Ping.Services.Google;

public record GooglePingInfo(string Name, string? Address, double? Lat, double? Lng);

public interface IPingNameService
{
    Task<string?> GetPingNameAsync(double lat, double lng);
    Task<List<GooglePingInfo>> SearchPingsAsync(string query, double lat, double lng, double radiusKm);
}

