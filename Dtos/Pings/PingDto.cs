using Ping.Dtos.Activities;
using Ping.Models.Pings;
using Ping.Models.Business;
using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Pings
{
    public record UpsertPingDto(
        [Required, MaxLength(100)] string Name,
        [MaxLength(256)] string? Address,
        [Range(-90, 90)] double Latitude,
        [Range(-180, 180)] double Longitude,
        PingVisibility Visibility,
        PingType Type,
        int? PingGenreId,
        [MaxLength(256)] string? GooglePlaceId
    );
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
        ClaimStatus? ClaimStatus = null,
        bool IsClaimed = false,
        int? PingGenreId = null,
        string? PingGenreName = null,
        string? GooglePlaceId = null,
        bool IsPingDeleted = false,
        string? ThumbnailUrl = null
    )
    {
        /// <summary>
        /// Single source of truth for whether a ping is verified. Derived from
        /// <see cref="Type"/> so it always reflects the authoritative flag — clients
        /// should rely on this rather than inferring verification from GooglePlaceId.
        /// </summary>
        public bool IsVerified => Type == PingType.Verified;
    }

    public class UpdatePingDto
    {
        [MaxLength(100)] public string? Name { get; set; }
        public int? PingGenreId { get; set; }
    }
}
