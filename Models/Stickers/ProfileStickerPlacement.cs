using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Stickers;

/// <summary>
/// A single placed sticker on a user's profile header. Coordinates are normalized
/// (0..1) relative to the header canvas so they scale across device sizes.
/// A user may place the same sticker multiple times, so each placement is its own row.
/// </summary>
public class ProfileStickerPlacement
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string StickerId { get; set; } = string.Empty;

    public Sticker? Sticker { get; set; }

    /// <summary>Normalized horizontal center position, 0..1 of header width.</summary>
    public double X { get; set; }

    /// <summary>Normalized vertical center position, 0..1 of header height.</summary>
    public double Y { get; set; }

    public double Scale { get; set; } = 1.0;

    /// <summary>Rotation in degrees.</summary>
    public double Rotation { get; set; }

    public int ZIndex { get; set; }
}
