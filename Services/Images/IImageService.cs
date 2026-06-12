using Microsoft.AspNetCore.Http;

namespace Ping.Services.Images;

public interface IImageService
{
    /// <summary>
    /// Processes an uploaded image: validate, uploads original, creates/uploads thumbnail.
    /// Returns (OriginalUrl, ThumbnailUrl).
    /// </summary>
    Task<(string OriginalUrl, string ThumbnailUrl)> ProcessAndUploadImageAsync(IFormFile file, string folder, string userId);

    /// <summary>
    /// Downloads an already-uploaded image by URL, generates a thumbnail and uploads it.
    /// Used when an existing image (which has no thumbnail of its own) is promoted to be
    /// a cover. Returns the new thumbnail URL, or the original URL if generation fails.
    /// </summary>
    Task<string> GenerateThumbnailFromUrlAsync(string imageUrl, string folder, string userId);
}
