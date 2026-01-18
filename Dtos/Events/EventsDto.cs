using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Events;

public record EventDto(
    int Id,
    string Title,
    string? Description,
    bool IsPublic,
    DateTime StartTime,
    DateTime EndTime,
    string Location,
    UserSummaryDto CreatedBy,
    DateTime CreatedAt,
    List<EventAttendeeDto> Attendees,
    string Status,
    double latitude,
    double longitude,
    int PingId,
    int? EventGenreId,
    string? EventGenreName,
    string? ImageUrl,
    string? ThumbnailUrl,
    double? Price,
    bool IsHosting,
    List<string> FriendThumbnails,
    string? Address
);

public record UserSummaryDto(
    string Id,
    string UserName,
    string? ProfilePictureUrl
);

public record EventAttendeeDto(
    string Id,
    string UserName,
    string? ProfilePictureUrl,
    string Status
);

public record CreateEventDto(
    [Required, MaxLength(100)] string Title,
    [MaxLength(500)] string? Description,
    bool IsPublic,
    DateTime StartTime,
    DateTime EndTime,
    int PingId,
    int? EventGenreId,
    string? ImageUrl,
    string? ThumbnailUrl,
    double? Price
);

public class UpdateEventDto
{
    [MaxLength(100)] public string? Title { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public bool? IsPublic { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? PingId { get; set; }
    public int? EventGenreId { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public double? Price { get; set; }
}

