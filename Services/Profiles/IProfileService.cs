using Ping.Dtos.Profiles;
using Microsoft.AspNetCore.Http; // Add this for IFormFile
using Ping.Dtos.Common;
using Ping.Dtos.Reviews;
using Ping.Dtos.Pings;
using Ping.Dtos.Events;

namespace Ping.Services.Profiles;

public interface IProfileService
{
    Task<PersonalProfileDto> GetMyProfileAsync(string userId);
    Task<List<ProfileDto>> SearchProfilesAsync(string query, string currentUsername);
    Task<string> UpdateProfileImageAsync(string userId, IFormFile file);
    Task<ProfileDto> GetProfileByIdAsync(string targetUserId, string currentUserId);
    Task<QuickProfileDto> GetQuickProfileAsync(string targetUserId, string currentUserId);
    Task<PaginatedResult<PingDetailsDto>> GetUserPingsAsync(string targetUserId, string currentUserId, PaginationParams pagination);
    Task<PaginatedResult<EventDto>> GetUserEventsAsync(string targetUserId, string currentUserId, PaginationParams pagination);
    Task<PaginatedResult<PlaceReviewSummaryDto>> GetProfilePlacesAsync(string targetUserId, string currentUserId, PaginationParams pagination, string? sortBy = null, string? sortOrder = null);
    Task<PaginatedResult<ReviewDto>> GetProfilePlaceReviewsAsync(string targetUserId, int pingId, string currentUserId, PaginationParams pagination);
    Task UpdateProfilePrivacyAsync(string userId, PrivacySettingsDto settings);
}

