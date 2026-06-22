using Microsoft.AspNetCore.Http;
using Ping.Dtos.Stickers;

namespace Ping.Services.Stickers;

public interface IStickerService
{
    /// <summary>All active stickers in the catalog.</summary>
    Task<List<StickerDto>> GetCatalogAsync();

    /// <summary>
    /// Stickers the user owns. Ensures badge stickers (verified / founder) are granted
    /// to qualifying users before returning.
    /// </summary>
    Task<List<StickerDto>> GetOwnedStickersAsync(string userId);

    /// <summary>Placements to render on a user's profile header.</summary>
    Task<List<ProfileStickerPlacementDto>> GetPlacementsAsync(string userId);

    /// <summary>
    /// Replaces the user's profile header placements. Throws <see cref="ArgumentException"/>
    /// if any placement references a sticker the user does not own.
    /// </summary>
    Task SavePlacementsAsync(string userId, List<SaveStickerPlacementDto> placements);

    /// <summary>Creates a new sticker catalog entry.</summary>
    Task<StickerDto> CreateStickerAsync(string key, string name, string? category, IFormFile? file, string adminUserId);

    /// <summary>Gets a sticker by its unique database ID.</summary>
    Task<StickerDto?> GetStickerByIdAsync(string id);

    /// <summary>Lists all stickers in the system, active and inactive (for admins).</summary>
    Task<List<StickerDto>> GetAllStickersForAdminAsync();

    /// <summary>Toggles whether a sticker is active in the catalog.</summary>
    Task<StickerDto> ToggleStickerActiveAsync(string id);

    /// <summary>Sets whether a sticker is part of the marketplace rotation.</summary>
    Task<StickerDto> SetStickerRotationAsync(string id, bool inRotation);

    /// <summary>Active stickers in the current marketplace rotation, flagged with whether
    /// the given user already owns each.</summary>
    Task<List<MarketplaceStickerDto>> GetMarketplaceAsync(string userId);

    /// <summary>Grants the current user a sticker from the rotation. Idempotent. Throws
    /// <see cref="KeyNotFoundException"/> if the sticker is missing, inactive, or not in rotation.</summary>
    Task ClaimStickerAsync(string userId, string stickerId);

    /// <summary>Grants user ownership to a specific sticker.</summary>
    Task GrantStickerOwnershipAsync(string userIdentifier, string stickerId);

    /// <summary>Deletes a sticker and all associated user ownerships/placements.</summary>
    Task DeleteStickerAsync(string id);
}
