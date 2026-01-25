using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Activities
{
    public record PingActivitySummaryDto(
        int Id,
        string Name,
        int? PingGenreId,
        string? PingGenreName
    );
    public record CreatePingActivityDto(
        int PingId,
        [Required, MaxLength(100)] string Name,
        int? PingGenreId
    );

    public record PingActivityDetailsDto(
        int Id,
        int PingId, 
        string Name,
        int? PingGenreId,
        string? PingGenreName,
        DateTime CreatedUtc,
        string? WarningMessage = null,
        double AvgRating = 0
    );
    
    public record PingGenreDto(int Id, string Name);

    public record CreatePingGenreDto(
        [Required, MaxLength(100)] string Name
    );

    public class ActivitySearchDto : Ping.Dtos.Common.PaginationParams
    {
        public string? Query { get; set; }
        public int? PingGenreId { get; set; }
        public int? PingId { get; set; }
    }
}

