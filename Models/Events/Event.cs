using Ping.Models.AppUsers;
using Ping.Models.Pings;

namespace Ping.Models.Events;

public class Event
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public bool IsPublic { get; set; } 
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public decimal? Price { get; set; }

    public required string CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }

    // Many-to-many via join entity (recommended)
    public List<EventAttendee> Attendees { get; set; } = new();
    public string Status { get; set;  } = string.Empty;

    public int? EventGenreId { get; set; }
    public EventGenre? EventGenre { get; set; }

    public int PingId { get; set; }
    public Ping.Models.Pings.Ping Ping { get; set; } = null!;
}
