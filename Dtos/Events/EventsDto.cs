namespace Conquest.Dtos.Events;

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
    List<UserSummaryDto> Attendees,
    string Status,
    double latitude,
    double longitude,
    int? PlaceId
);

public record UserSummaryDto(
    string Id,
    string UserName,
    string? FirstName,
    string? LastName
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
    int? PlaceId
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
    public int? PlaceId { get; set; }
}
