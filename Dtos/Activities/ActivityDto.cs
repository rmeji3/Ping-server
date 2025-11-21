using System.ComponentModel.DataAnnotations;

namespace Conquest.Dtos.Activities
{
    public record ActivitySummaryDto(
        int Id,
        string Name,
        int? ActivityKindId,
        string? ActivityKindName
    );
    public record CreateActivityDto(
        int PlaceId,
        [MaxLength(100)] string Name,
        int? ActivityKindId
    );

    public record ActivityDetailsDto(
        int Id,
        int PlaceId, 
        string Name,
        int? ActivityKindId,
        string? ActivityKindName,
        DateTime CreatedUtc
    );
    
    public record ActivityKindDto(int Id, string Name);

    public record CreateActivityKindDto(
        [Required, MaxLength(100)] string Name
    );
}
