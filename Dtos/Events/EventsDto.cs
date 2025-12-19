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
    int? PingId,
    int? EventGenreId,
    string? EventGenreName
)
{
    public bool IsAdHoc => PingId == null;
}

public record UserSummaryDto(
    string Id,
    string UserName,
    string? FirstName,
    string? LastName,
    string? ProfilePictureUrl
);

public record EventAttendeeDto(
    string Id,
    string UserName,
    string? FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    string Status
);

public record CreateEventDto(
    string Title,
    string? Description,
    bool IsPublic,
    DateTime StartTime,
    DateTime EndTime,
    string Location,
    double Latitude,
    double Longitude,
    int? PingId,
    int? EventGenreId
);

public class UpdateEventDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool? IsPublic { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? PingId { get; set; }
    public int? EventGenreId { get; set; }
}

