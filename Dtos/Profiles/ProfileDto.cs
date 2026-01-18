using System.ComponentModel.DataAnnotations;
using Ping.Dtos.Reviews;
using Ping.Dtos.Pings;
using Ping.Dtos.Events;
using Ping.Models.AppUsers;

namespace Ping.Dtos.Profiles;

public record ProfileDto(
    [Required] string Id,
    [Required] string DisplayName,
    string? ProfilePictureUrl,
    string? Bio,
    FriendshipStatus FriendshipStatus,
    int ReviewCount,
    int PingCount,
    int EventCount,
    int FollowersCount,
    int FollowingCount,
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
    Blocked,
    Following
}

public record QuickProfileDto(
    [Required] string Id,
    [Required] string DisplayName,
    string? ProfilePictureUrl,
    string? Bio,
    FriendshipStatus FriendshipStatus,
    int ReviewCount,
    int PingCount,
    int EventCount,
    int FollowersCount,
    int FollowingCount,
    bool IsFriends,
    PrivacyConstraint ReviewsPrivacy,
    PrivacyConstraint PingsPrivacy,
    PrivacyConstraint LikesPrivacy
);

public record PersonalProfileDto(
    [Required] string Id,
    [Required] string DisplayName,
    string? ProfilePictureUrl,
    string? Bio,
    [Required] string Email,
    int FollowersCount,
    int FollowingCount,
    string[] Roles
);

public record UpdateBioDto(
    [MaxLength(256)] string? Bio
);
