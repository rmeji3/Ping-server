using Ping.Models.AppUsers;

namespace Ping.Dtos.Profiles;

public record PrivacySettingsDto(
    PrivacyConstraint? ReviewsPrivacy,
    PrivacyConstraint? PingsPrivacy,
    PrivacyConstraint? LikesPrivacy
);
