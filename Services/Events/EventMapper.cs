using Conquest.Dtos.Events;
using Conquest.Models.AppUsers;
using Conquest.Models.Events;

namespace Conquest.Services.Events;

public static class EventMapper
{
    public static EventDto MapToDto(Event ev, UserSummaryDto creatorSummary, Dictionary<string, AppUser> attendeeMap, string? currentUserId)
    {
        var attendeeSummaries = ev.Attendees
            .Where(a => attendeeMap.ContainsKey(a.UserId))
            .Select(a =>
            {
                var u = attendeeMap[a.UserId];
                return new UserSummaryDto(
                    u.Id,
                    u.UserName!,
                    u.FirstName,
                    u.LastName
                );
            })
            .ToList();

        // Determine status based on currentUserId
        string status;
        if (currentUserId == null)
        {
            status = "unknown";
        }
        else if (ev.CreatedById == currentUserId)
        {
            status = "mine";
        }
        else if (ev.Attendees.Any(a => a.UserId == currentUserId))
        {
            status = "attending";
        }
        else
        {
            status = "not-attending";
        }

        return new EventDto(
            ev.Id,
            ev.Title,
            ev.Description,
            ev.IsPublic,
            ev.StartTime,
            ev.EndTime,
            ev.Location,
            creatorSummary,
            ev.CreatedAt,
            attendeeSummaries,
            status,
            ev.Latitude,
            ev.Longitude,
            ev.PlaceId
        );
    }
}
