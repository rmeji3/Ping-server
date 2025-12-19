using Ping.Dtos.Activities;
using Ping.Models.Pings;
using Ping.Models.Business;

namespace Ping.Dtos.Pings
{
    public record UpsertPingDto(string Name, string? Address, double Latitude, double Longitude, PingVisibility Visibility, PingType Type, int? PingGenreId);
    public record PingDetailsDto(
        int Id, 
        string Name, 
        string Address, 
        double Latitude, 
        double Longitude, 
        PingVisibility Visibility,
        PingType Type,
        bool IsOwner,
        bool IsFavorited,
        int Favorites,
        PingActivitySummaryDto[] Activities,
        string[] PingGenres, // Kept as array if needed or just single
        ClaimStatus? ClaimStatus = null,
        bool IsClaimed = false,
        int? PingGenreId = null,
        string? PingGenreName = null
        );

}

