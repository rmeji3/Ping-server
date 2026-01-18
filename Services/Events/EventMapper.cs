using Ping.Dtos.Events;
using Ping.Models.AppUsers;
using Ping.Models.Events;

namespace Ping.Services.Events;

public static class EventMapper
{
    public static EventDto MapToDto(Event ev, UserSummaryDto creatorSummary, Dictionary<string, AppUser> attendeeMap, string? currentUserId, IReadOnlyList<string>? viewerFriendIds = null)
    {
        var attendeeSummaries = new List<EventAttendeeDto>();
        var friendThumbnails = new List<string>();

        if (ev.Attendees != null)
        {
            foreach (var att in ev.Attendees)
            {
                if (attendeeMap.TryGetValue(att.UserId, out var u))
                {
                    attendeeSummaries.Add(new EventAttendeeDto(
                        u.Id,
                        u.UserName!,
                        u.ProfileImageUrl,
                        att.Status.ToString().ToLower()
                    ));

                    // Check if friend (only if attending?)
                    // "show that your friends are attending" -> implies status Attending
                    if (viewerFriendIds != null && 
                        viewerFriendIds.Contains(u.Id) && 
                        att.Status == AttendeeStatus.Attending &&
                        friendThumbnails.Count < 5)
                    {
                        if (!string.IsNullOrEmpty(u.ProfileThumbnailUrl))
                        {
                            friendThumbnails.Add(u.ProfileThumbnailUrl);
                        }
                        else if (!string.IsNullOrEmpty(u.ProfileImageUrl))
                        {
                             friendThumbnails.Add(u.ProfileImageUrl);
                        }
                    }
                }
            }
        }

        // Determine status based on currentUserId
        string status;
        bool isHosting = false;

        if (currentUserId == null)
        {
            status = "unknown";
        }
        else if (ev.CreatedById == currentUserId)
        {
            status = "mine";
            isHosting = true;
        }
        else if (ev.Attendees != null && ev.Attendees.Any(a => a.UserId == currentUserId))
        {
            status = "attending";
            // Check specific status?
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
            ev.Ping.Name,
            creatorSummary,
            ev.CreatedAt,
            attendeeSummaries,
            status,
            ev.Ping.Latitude,
            ev.Ping.Longitude,
            ev.PingId,
            ev.EventGenreId,
            ev.EventGenre?.Name,
            ev.ImageUrl,
            ev.ThumbnailUrl,
            ev.Price,
            isHosting,
            friendThumbnails,
            ev.Ping.Address
        );
    }
}

