using System.ComponentModel.DataAnnotations;
using Conquest.Dtos.Reviews;
using Conquest.Dtos.Places;
using Conquest.Dtos.Events;

namespace Conquest.Dtos.Profiles;

public record ProfileDto(
    [Required] string Id,
    [Required] string DisplayName,
    [Required] string FirstName,
    [Required] string LastName,
    string? ProfilePictureUrl,
    List<ReviewDto>? Reviews,
    List<PlaceDetailsDto>? Places,
    List<EventDto>? Events,
    FriendshipStatus FriendshipStatus,
    int ReviewCount,
    int PlaceVisitCount,
    int EventCount,
    bool IsFriends,
    PrivacyConstraint ReviewsPrivacy,
    PrivacyConstraint PlacesPrivacy,
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
    int PlaceVisitCount,
    int EventCount,
    bool IsFriends,
    PrivacyConstraint ReviewsPrivacy,
    PrivacyConstraint PlacesPrivacy,
    PrivacyConstraint LikesPrivacy
);

public enum PrivacyConstraint
{
    Public = 0,
    FriendsOnly = 1,
    Private = 2
}
public record PersonalProfileDto(
    [Required] string Id,
    [Required] string DisplayName,
    [Required] string FirstName,
    [Required] string LastName,
    string? ProfilePictureUrl,
    [Required] string Email,
    List<EventDto> Events
);