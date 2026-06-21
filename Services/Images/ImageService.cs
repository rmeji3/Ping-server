using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;
using Ping.Services.Storage;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Images;

public class ImageService(IStorageService storageService, HttpClient httpClient, ILogger<ImageService> logger) : IImageService
{
    private const int MaxThumbnailSize = 500;
    private const long MaxFileSize = 10 * 1024 * 1024; // 10MB

    public async Task<(string OriginalUrl, string ThumbnailUrl)> ProcessAndUploadImageAsync(IFormFile file, string folder, string userId)
    {
        // 1. Validation
        if (file.Length > MaxFileSize)
            throw new ArgumentException($"File size exceeds {MaxFileSize / 1024 / 1024}MB limit.");

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif", "image/heic", "image/heif" };
        if (!allowedTypes.Contains(file.ContentType))
            throw new ArgumentException("Invalid file type. Only JPEG, PNG, WebP, GIF, HEIC, and HEIF are allowed.");

        // 2. Generate Keys
        var ext = Path.GetExtension(file.FileName);
        var timestamp = DateTime.UtcNow.Ticks;
        var originalKey = $"{folder}/{userId}/{timestamp}_orig{ext}";
        var thumbKey = $"{folder}/{userId}/{timestamp}_thumb{ext}";

        // 3. Decode the image so we can bake in EXIF orientation. Link unfurlers
        //    / OG-image fetchers (and other non-EXIF-aware consumers) render the
        //    raw pixels, so a photo carrying a "rotate 90°" EXIF tag shows up
        //    sideways unless we apply the rotation to the pixels here.
        string originalUrl;
        string thumbUrl;
        try
        {
            // Detect the source format so we can re-save the original in kind.
            IImageFormat format;
            using (var detectStream = file.OpenReadStream())
                format = await Image.DetectFormatAsync(detectStream);

            using var imageStream = file.OpenReadStream();
            using var image = await Image.LoadAsync(imageStream);

            // Bake EXIF orientation into the pixels, then drop the now-stale tag.
            image.Mutate(x => x.AutoOrient());

            // Upload the orientation-corrected original in its original format.
            using (var origOut = new MemoryStream())
            {
                await image.SaveAsync(origOut, format);
                origOut.Position = 0;
                var origFile = new FormFile(origOut, 0, origOut.Length, "file", $"original{ext}")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = file.ContentType
                };
                originalUrl = await storageService.UploadFileAsync(origFile, originalKey);
            }

            // Generate & upload the thumbnail (already upright after AutoOrient).
            if (image.Width > MaxThumbnailSize || image.Height > MaxThumbnailSize)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxThumbnailSize, MaxThumbnailSize)
                }));
            }

            using (var outStream = new MemoryStream())
            {
                await image.SaveAsWebpAsync(outStream);
                outStream.Position = 0;
                var thumbFile = new FormFile(outStream, 0, outStream.Length, "file", $"thumbnail.webp")
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "image/webp"
                };
                thumbUrl = await storageService.UploadFileAsync(thumbFile, Path.ChangeExtension(thumbKey, ".webp"));
            }
        }
        catch (UnknownImageFormatException)
        {
            // For formats ImageSharp can't decode (e.g. HEIC), upload the raw
            // original and reuse it as its own thumbnail.
            originalUrl = await storageService.UploadFileAsync(file, originalKey);
            thumbUrl = originalUrl;
            logger.LogInformation("Unsupported image format for processing, using raw original as thumbnail");
        }

        logger.LogInformation("Uploaded image {OriginalKey} and thumbnail {ThumbKey}", originalKey, thumbKey);

        return (originalUrl, thumbUrl);
    }

    public async Task<string> GenerateThumbnailFromUrlAsync(string imageUrl, string folder, string userId)
    {
        try
        {
            using var response = await httpClient.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();

            await using var sourceStream = await response.Content.ReadAsStreamAsync();
            using var image = await Image.LoadAsync(sourceStream);

            // Bake in EXIF orientation so the thumbnail is always upright.
            image.Mutate(x => x.AutoOrient());

            if (image.Width > MaxThumbnailSize || image.Height > MaxThumbnailSize)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxThumbnailSize, MaxThumbnailSize)
                }));
            }

            using var outStream = new MemoryStream();
            await image.SaveAsWebpAsync(outStream);
            outStream.Position = 0;

            var thumbKey = $"{folder}/{userId}/{DateTime.UtcNow.Ticks}_thumb.webp";
            var thumbFile = new FormFile(outStream, 0, outStream.Length, "file", "thumbnail.webp")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/webp"
            };

            var thumbUrl = await storageService.UploadFileAsync(thumbFile, thumbKey);
            logger.LogInformation("Generated thumbnail {ThumbKey} from existing image {Url}", thumbKey, imageUrl);
            return thumbUrl;
        }
        catch (Exception ex)
        {
            // Non-fatal: fall back to the original image as its own thumbnail so the
            // cover still renders (just at full resolution) instead of breaking.
            logger.LogWarning(ex, "Failed to generate thumbnail from URL {Url}; using original.", imageUrl);
            return imageUrl;
        }
    }
}
