using System.ComponentModel.DataAnnotations;
using Ping.Dtos.Pings;

namespace Ping.Dtos.Pings
{
    public record CollectionDto(
        int Id,
        string Name,
        bool IsPublic,
        int PingCount,
        string? ThumbnailUrl,
        DateTime CreatedUtc
    );

    public record CreateCollectionDto(
        [Required] string Name,
        bool IsPublic
    );

    public record UpdateCollectionDto(
        string? Name,
        bool? IsPublic
    );

    public record CollectionDetailsDto(
        int Id,
        string Name,
        bool IsPublic,
        int PingCount,
        string? ThumbnailUrl,
        DateTime CreatedUtc,
        List<PingDetailsDto> Pings
    );

    public record AddPingToCollectionDto(
        [Required] int PingId
    );
}
