using Conquest.Models.AppUsers;

namespace Conquest.Models.Events;

public class Event
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public bool IsPublic { get; set; } 
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public required string Location { get; set; }

    // FK to Identity user (string because IdentityUser<TKey> uses string by default)
    public required string CreatedById { get; set; }

    // optional: if you decide to map AppUser in AppDbContext too
    // public AppUser? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    // Many-to-many via join entity (recommended)
    public List<EventAttendee> Attendees { get; set; } = new();
    public string Status { get; set;  } = string.Empty;
    public required double Latitude { get; set;  }
    public required double Longitude { get; set;  }
}