using System.ComponentModel.DataAnnotations;
using Ping.Dtos.Reviews;
using Ping.Dtos.Pings;
using Ping.Dtos.Events;
using Ping.Models.AppUsers;

namespace Ping.Dtos.Profiles;

public record ProfileDto(
    [Required] string Id,
    [Required] string DisplayName,
    [Required] string FirstName,
    [Required] string LastName,
    string? ProfilePictureUrl,
    List<ReviewDto>? Reviews,
    List<PingDetailsDto>? Pings,
    List<EventDto>? Events,
    FriendshipStatus FriendshipStatus,
    int ReviewCount,
    int PingCount,
    int EventCount,
    bool IsFriends,
    PrivacyConstraint ReviewsPrivacy,
    PrivacyConstraint PingsPrivacy,
    PrivacyConstraint LikesPrivacy
);

public enum FriendshipStatus
{
    None,
    Pending,
    Accepted,
    Blocked
}

public record QuickProfileDto(
    [Required] string Id,
    [Required] string DisplayName,
    [Required] string FirstName,
    [Required] string LastName,
    string? ProfilePictureUrl,
    FriendshipStatus FriendshipStatus,
    int ReviewCount,
    int PingCount,
    int EventCount,
    bool IsFriends,
    PrivacyConstraint ReviewsPrivacy,
    PrivacyConstraint PingsPrivacy,
    PrivacyConstraint LikesPrivacy
);

public record PersonalProfileDto(
    [Required] string Id,
    [Required] string DisplayName,
    [Required] string FirstName,
    [Required] string LastName,
    string? ProfilePictureUrl,
    [Required] string Email,
    List<EventDto> Events,
    List<PingDetailsDto> Pings,
    List<ReviewDto> Reviews,
    string[] Roles
);
