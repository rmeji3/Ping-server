namespace Conquest.Models.Events;

public class EventAttendee
{
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;

    public string UserId { get; set; } = null!;
    public DateTime JoinedAt { get; set; }
    
    public AttendeeStatus Status { get; set; } = AttendeeStatus.Attending;
}

public enum AttendeeStatus
{
    Attending,
    Invited,
    Declined,
    Maybe
}
