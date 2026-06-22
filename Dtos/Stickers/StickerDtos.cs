using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Stickers;

public record StickerDto(
    string Id,
    string Key,
    string Name,
    string? ImageUrl,
    string? Category,
    bool IsActive = true,
    bool InRotation = false
);

/// <summary>A sticker shown in the marketplace rotation, with whether the current
/// user already owns (has claimed) it.</summary>
public record MarketplaceStickerDto(
    string Id,
    string Key,
    string Name,
    string? ImageUrl,
    string? Category,
    bool Owned
);

/// <summary>A placed sticker as returned for display. Includes sticker metadata so the
/// client can render it without separately joining the catalog.</summary>
public record ProfileStickerPlacementDto(
    string StickerId,
    string Key,
    string? ImageUrl,
    double X,
    double Y,
    double Scale,
    double Rotation,
    int ZIndex
);

/// <summary>A single placement as sent by the client when saving. Sticker metadata is
/// resolved server-side from <see cref="StickerId"/>.</summary>
public record SaveStickerPlacementDto(
    [Required] string StickerId,
    double X,
    double Y,
    double Scale,
    double Rotation,
    int ZIndex
);

public record SaveProfileStickerPlacementsDto(
    [Required] List<SaveStickerPlacementDto> Placements
);
