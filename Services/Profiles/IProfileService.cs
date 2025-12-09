using Conquest.Dtos.Profiles;
using Microsoft.AspNetCore.Http; // Add this for IFormFile

namespace Conquest.Services.Profiles;

public interface IProfileService
{
    Task<PersonalProfileDto> GetMyProfileAsync(string userId);
    Task<List<ProfileDto>> SearchProfilesAsync(string query, string currentUsername);
    Task<string> UpdateProfileImageAsync(string userId, IFormFile file);
    Task<ProfileDto> GetProfileByIdAsync(string targetUserId, string currentUserId);
    Task<QuickProfileDto> GetQuickProfileAsync(string targetUserId, string currentUserId);
}
