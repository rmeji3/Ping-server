using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ping.Data.App;
using Ping.Dtos.Stickers;
using Ping.Models.AppUsers;
using Ping.Models.Stickers;
using Ping.Services.Images;

namespace Ping.Services.Stickers;

public class StickerService : IStickerService
{
    private readonly AppDbContext _context;
    private readonly UserManager<AppUser> _userManager;
    private readonly IImageService _imageService;

    // Catalog keys for the seeded badge stickers.
    public const string VerifiedBadgeKey = "verified_badge";
    public const string FounderBadgeKey = "founder_badge";

    public StickerService(AppDbContext context, UserManager<AppUser> userManager, IImageService imageService)
    {
        _context = context;
        _userManager = userManager;
        _imageService = imageService;
    }

    public async Task<List<StickerDto>> GetCatalogAsync()
    {
        return await _context.Stickers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new StickerDto(s.Id, s.Key, s.Name, s.ImageUrl, s.Category, s.IsActive, s.InRotation))
            .ToListAsync();
    }

    public async Task<List<StickerDto>> GetOwnedStickersAsync(string userId)
    {
        await EnsureBadgeGrantsAsync(userId);

        return await _context.UserStickers
            .Where(us => us.UserId == userId && us.Sticker != null && us.Sticker.IsActive)
            .OrderByDescending(us => us.AcquiredUtc)
            .Select(us => new StickerDto(
                us.Sticker!.Id, us.Sticker.Key, us.Sticker.Name, us.Sticker.ImageUrl, us.Sticker.Category, us.Sticker.IsActive, us.Sticker.InRotation))
            .ToListAsync();
    }

    public async Task<List<ProfileStickerPlacementDto>> GetPlacementsAsync(string userId)
    {
        return await _context.ProfileStickerPlacements
            .Where(p => p.UserId == userId && p.Sticker != null && p.Sticker.IsActive)
            .OrderBy(p => p.ZIndex)
            .Select(p => new ProfileStickerPlacementDto(
                p.StickerId, p.Sticker!.Key, p.Sticker.ImageUrl, p.X, p.Y, p.Scale, p.Rotation, p.ZIndex))
            .ToListAsync();
    }

    public async Task SavePlacementsAsync(string userId, List<SaveStickerPlacementDto> placements)
    {
        await EnsureBadgeGrantsAsync(userId);

        var ownedStickerIds = await _context.UserStickers
            .Where(us => us.UserId == userId)
            .Select(us => us.StickerId)
            .ToHashSetAsync();

        // Reject placements referencing stickers the user does not own.
        var invalid = placements.Where(p => !ownedStickerIds.Contains(p.StickerId)).ToList();
        if (invalid.Count > 0)
        {
            throw new ArgumentException("One or more placements reference stickers you do not own.");
        }

        // Replace all existing placements for this user.
        var existing = _context.ProfileStickerPlacements.Where(p => p.UserId == userId);
        _context.ProfileStickerPlacements.RemoveRange(existing);

        var index = 0;
        foreach (var p in placements)
        {
            _context.ProfileStickerPlacements.Add(new ProfileStickerPlacement
            {
                UserId = userId,
                StickerId = p.StickerId,
                X = Math.Clamp(p.X, 0.0, 1.0),
                Y = Math.Clamp(p.Y, 0.0, 1.0),
                Scale = Math.Clamp(p.Scale <= 0 ? 1.0 : p.Scale, 0.2, 5.0),
                Rotation = p.Rotation,
                ZIndex = p.ZIndex != 0 ? p.ZIndex : index,
            });
            index++;
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Grants badge stickers to users who qualify (verified / founding member) but don't
    /// yet own them. Idempotent — safe to call on every fetch.
    /// </summary>
    private async Task EnsureBadgeGrantsAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return;

        var qualifyingKeys = new List<string>();
        if (user.IsVerified) qualifyingKeys.Add(VerifiedBadgeKey);
        if (user.IsFoundingMember && user.EmailConfirmed) qualifyingKeys.Add(FounderBadgeKey);
        if (qualifyingKeys.Count == 0) return;

        var qualifyingStickers = await _context.Stickers
            .Where(s => qualifyingKeys.Contains(s.Key) && s.IsActive)
            .ToListAsync();

        var ownedIds = await _context.UserStickers
            .Where(us => us.UserId == userId)
            .Select(us => us.StickerId)
            .ToHashSetAsync();

        var toGrant = qualifyingStickers.Where(s => !ownedIds.Contains(s.Id)).ToList();
        if (toGrant.Count == 0) return;

        foreach (var sticker in toGrant)
        {
            _context.UserStickers.Add(new UserSticker
            {
                UserId = userId,
                StickerId = sticker.Id,
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task<StickerDto> CreateStickerAsync(string key, string name, string? category, IFormFile? file, string adminUserId)
    {
        var normalizedKey = key.Trim().ToLowerInvariant();
        if (await _context.Stickers.AnyAsync(s => s.Key == normalizedKey))
        {
            throw new ArgumentException($"A sticker with key '{normalizedKey}' already exists.");
        }

        string? imageUrl = null;
        if (file != null)
        {
            var (originalUrl, _) = await _imageService.ProcessAndUploadImageAsync(file, "stickers", adminUserId);
            imageUrl = originalUrl;
        }

        var sticker = new Sticker
        {
            Key = normalizedKey,
            Name = name.Trim(),
            Category = category?.Trim(),
            ImageUrl = imageUrl,
            IsActive = true
        };

        _context.Stickers.Add(sticker);
        await _context.SaveChangesAsync();

        return new StickerDto(sticker.Id, sticker.Key, sticker.Name, sticker.ImageUrl, sticker.Category, sticker.IsActive);
    }

    public async Task<StickerDto?> GetStickerByIdAsync(string id)
    {
        var sticker = await _context.Stickers.FindAsync(id);
        if (sticker == null) return null;

        return new StickerDto(sticker.Id, sticker.Key, sticker.Name, sticker.ImageUrl, sticker.Category, sticker.IsActive);
    }

    public async Task<List<StickerDto>> GetAllStickersForAdminAsync()
    {
        return await _context.Stickers
            .OrderBy(s => s.Name)
            .Select(s => new StickerDto(s.Id, s.Key, s.Name, s.ImageUrl, s.Category, s.IsActive, s.InRotation))
            .ToListAsync();
    }

    public async Task<StickerDto> SetStickerRotationAsync(string id, bool inRotation)
    {
        var sticker = await _context.Stickers.FindAsync(id);
        if (sticker == null)
        {
            throw new KeyNotFoundException($"Sticker with ID {id} not found.");
        }

        sticker.InRotation = inRotation;
        await _context.SaveChangesAsync();

        return new StickerDto(sticker.Id, sticker.Key, sticker.Name, sticker.ImageUrl, sticker.Category, sticker.IsActive, sticker.InRotation);
    }

    public async Task<List<MarketplaceStickerDto>> GetMarketplaceAsync(string userId)
    {
        var ownedIds = await _context.UserStickers
            .Where(us => us.UserId == userId)
            .Select(us => us.StickerId)
            .ToHashSetAsync();

        return await _context.Stickers
            .Where(s => s.IsActive && s.InRotation)
            .OrderBy(s => s.Name)
            .Select(s => new MarketplaceStickerDto(
                s.Id, s.Key, s.Name, s.ImageUrl, s.Category, ownedIds.Contains(s.Id)))
            .ToListAsync();
    }

    public async Task ClaimStickerAsync(string userId, string stickerId)
    {
        var sticker = await _context.Stickers.FindAsync(stickerId);
        if (sticker == null || !sticker.IsActive || !sticker.InRotation)
        {
            throw new KeyNotFoundException($"Sticker with ID {stickerId} is not available to claim.");
        }

        var alreadyOwns = await _context.UserStickers.AnyAsync(us => us.UserId == userId && us.StickerId == stickerId);
        if (alreadyOwns)
        {
            return; // Idempotent success
        }

        _context.UserStickers.Add(new UserSticker
        {
            UserId = userId,
            StickerId = stickerId,
            AcquiredUtc = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    public async Task<StickerDto> ToggleStickerActiveAsync(string id)
    {
        var sticker = await _context.Stickers.FindAsync(id);
        if (sticker == null)
        {
            throw new KeyNotFoundException($"Sticker with ID {id} not found.");
        }

        sticker.IsActive = !sticker.IsActive;
        await _context.SaveChangesAsync();

        return new StickerDto(sticker.Id, sticker.Key, sticker.Name, sticker.ImageUrl, sticker.Category, sticker.IsActive);
    }

    public async Task GrantStickerOwnershipAsync(string userIdentifier, string stickerId)
    {
        AppUser? user = null;
        if (Guid.TryParse(userIdentifier, out _))
        {
            user = await _userManager.FindByIdAsync(userIdentifier);
        }
        else
        {
            user = await _userManager.FindByNameAsync(userIdentifier);
        }

        if (user == null)
        {
            throw new KeyNotFoundException($"User '{userIdentifier}' not found.");
        }

        var sticker = await _context.Stickers.FindAsync(stickerId);
        if (sticker == null || !sticker.IsActive)
        {
            throw new KeyNotFoundException($"Sticker with ID {stickerId} not found or is inactive.");
        }

        // Check if already owns
        var alreadyOwns = await _context.UserStickers.AnyAsync(us => us.UserId == user.Id && us.StickerId == stickerId);
        if (alreadyOwns)
        {
            return; // Idempotent success
        }

        _context.UserStickers.Add(new UserSticker
        {
            UserId = user.Id,
            StickerId = stickerId,
            AcquiredUtc = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    public async Task DeleteStickerAsync(string id)
    {
        var sticker = await _context.Stickers.FindAsync(id);
        if (sticker == null)
        {
            throw new KeyNotFoundException($"Sticker with ID {id} not found.");
        }

        _context.Stickers.Remove(sticker);
        await _context.SaveChangesAsync();
    }
}
