using Conquest.Dtos.Profiles;
using Conquest.Models.AppUsers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Conquest.Services.Storage;

namespace Conquest.Services.Profiles;

public class ProfileService(UserManager<AppUser> userManager, ILogger<ProfileService> logger, IStorageService storageService) : IProfileService
{
    public async Task<PersonalProfileDto> GetMyProfileAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            logger.LogWarning("GetMyProfile failed: User {UserId} not found.", userId);
            throw new KeyNotFoundException("User not found.");
        }

        logger.LogDebug("Retrieved profile for {UserName}", user.UserName);

        return new PersonalProfileDto(
            user.Id,
            user.UserName!,
            user.FirstName,
            user.LastName,
            user.ProfileImageUrl,
            user.Email!
        );
    }

    public async Task<List<ProfileDto>> SearchProfilesAsync(string query, string currentUsername)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Username query parameter is required.");

        var normalized = query.ToUpper(); // match Identity normalization

        var users = await userManager.Users
            .AsNoTracking()
            .Where(u => u.NormalizedUserName!.StartsWith(normalized)
            && u.NormalizedUserName != currentUsername.ToUpper()) // exclude yourself
            .OrderBy(u => u.UserName)
            .Take(15)
            .Select(u => new ProfileDto(
                u.Id,
                u.UserName!,
                u.FirstName,
                u.LastName,
                u.ProfileImageUrl
            ))
            .ToListAsync();

        logger.LogDebug("Profile search for '{Query}' returned {Count} results.", query, users.Count);

        return users;
    }



    public async Task<string> UpdateProfileImageAsync(string userId, IFormFile file)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        // Validate file
        // 5MB limit
        if (file.Length > 5 * 1024 * 1024)
        {
            throw new ArgumentException("File size exceeds 5MB limit.");
        }

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType))
        {
            throw new ArgumentException("Invalid file type. Only JPEG, PNG, and WebP are allowed.");
        }

        // Generate key: profiles/{userId}/{timestamp}-{random}.ext
        var ext = Path.GetExtension(file.FileName);
        var key = $"profiles/{userId}/{DateTime.UtcNow.Ticks}{ext}";

        // Upload
        var url = await storageService.UploadFileAsync(file, key);

        // Update User
        user.ProfileImageUrl = url;
        await userManager.UpdateAsync(user);
        
        logger.LogInformation("Updated profile image for user {UserId} to {Url}", userId, url);

        return url;
    }
}
