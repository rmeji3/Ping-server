using Conquest.Models.Events;

namespace Conquest.Services.Events;

public class EventsService
{
    public static string ComputeStatus(Event ev)
    {
        var now = DateTime.UtcNow;

        if (now < ev.StartTime) return "Upcoming";
        if (now < ev.EndTime)   return "Ongoing";
        return "Ended";
    }
}