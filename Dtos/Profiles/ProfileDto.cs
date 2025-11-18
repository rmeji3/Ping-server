using System.ComponentModel.DataAnnotations;

namespace Conquest.Dtos.Profiles;

public record ProfileDto(
    [Required] string Id,
    [Required] string UserName,
    [Required] string FirstName,
    [Required] string LastName,
    string? ProfilePictureUrl
);
public record PersonalProfileDto(
    [Required] string Id,
    [Required] string UserName,
    [Required] string FirstName,
    [Required] string LastName,
    string? ProfilePictureUrl,
    [Required] string Email
);