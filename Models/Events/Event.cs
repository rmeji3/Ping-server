using Ping.Models.AppUsers;
using Ping.Models.Pings;
using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Events;

public class Event
{
    public int Id { get; set; }
    
    [MaxLength(100)]
    public required string Title { get; set; }
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    public bool IsPublic { get; set; } 
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    
    [MaxLength(2048)]
    public string? ImageUrl { get; set; }
    
    [MaxLength(2048)]
    public string? ThumbnailUrl { get; set; }
    
    public decimal? Price { get; set; }

    public required string CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }

    // Many-to-many via join entity (recommended)
    public List<EventAttendee> Attendees { get; set; } = new();
    
    [MaxLength(50)]
    public string Status { get; set;  } = string.Empty;

    public int? EventGenreId { get; set; }
    public EventGenre? EventGenre { get; set; }

    public int PingId { get; set; }
    public Ping.Models.Pings.Ping Ping { get; set; } = null!;
}
