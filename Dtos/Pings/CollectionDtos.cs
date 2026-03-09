using System.ComponentModel.DataAnnotations;
using Ping.Dtos.Pings;

namespace Ping.Dtos.Pings
{
    public record CollectionDto(
        int Id,
        string Name,
        bool IsPublic,
        int PingCount,
        string? ImageUrl,
        string? ThumbnailUrl,
        DateTime CreatedUtc,
        bool IsSystem = false,
        bool IsOwner = false,
        bool IsSaved = false
    );

    public record CreateCollectionDto(
        [Required, MaxLength(100)] string Name,
        bool IsPublic,
        [MaxLength(2048)] string? ImageUrl = null,
        [MaxLength(2048)] string? ThumbnailUrl = null
    );

    public record UpdateCollectionDto(
        [MaxLength(100)] string? Name,
        bool? IsPublic,
        [MaxLength(2048)] string? ImageUrl,
        [MaxLength(2048)] string? ThumbnailUrl
    );

    public record CollectionDetailsDto(
        int Id,
        string Name,
        bool IsPublic,
        int PingCount,
        string? ImageUrl,
        string? ThumbnailUrl,
        DateTime CreatedUtc,
        List<PingDetailsDto> Pings,
        bool IsSystem = false,
        bool IsOwner = false,
        bool IsSaved = false
    );

    public record AddPingToCollectionDto(
        [Required] int PingId
    );
}
