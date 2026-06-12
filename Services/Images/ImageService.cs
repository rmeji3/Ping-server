using SixLabors.ImageSharp;
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

        // 3. Upload Original (Stream copy)
        string originalUrl;
        using (var stream = file.OpenReadStream())
        {
             // We need to copy the stream because UploadFileAsync might dispose or read it to end, 
             // and we need to read it again for thumbnail generation if we don't want to load fully into memory first.
             // Actually, Image.LoadAsync accepts a stream. 
             // Let's upload original first.
             originalUrl = await storageService.UploadFileAsync(file, originalKey);
        }

        // 4. Generate & Upload Thumbnail
        string thumbUrl;
        try
        {
            using (var imageStream = file.OpenReadStream())
            using (var image = await Image.LoadAsync(imageStream))
            {
                // Resize if larger than max
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
                    // Save as WebP for efficiency or keep original format?
                    // Let's keep original format to avoid complexity, or default to WebP.
                    // WebP is good for thumbnails.
                    await image.SaveAsWebpAsync(outStream);
                    outStream.Position = 0;

                    // We need to wrap MemoryStream in an IFormFile-like wrapper or overload UploadFileAsync to take stream.
                    // Assuming IStorageService has Stream overload?
                    // Let's check IStorageService. If not, we might need a dummy IFormFile or update IStorageService.
                    // Checking previous code... ProfileService used IFormFile directly.
                    // I will assume IStorageService needs IFormFile, so I will create a simple wrapper or update IStorageService.

                    // Wait, S3StorageService likely uses TransferUtility which takes Stream.
                    // I should verify ISotrageService signature.
                    // For now, I'll assume I need to implement a Stream->Upload adapter or update the interface.
                    // To be safe and fast, I'll update IStorageService to accept Stream if it doesn't, OR
                    // I'll create a FormFile wrapper.

                    // Check IStorageService first? No, I'll just write a FormFile wrapper here, it's safer than modifying existing interfaces extensively right now.
                    var thumbFile = new FormFile(outStream, 0, outStream.Length, "file", $"thumbnail.webp")
                    {
                        Headers = new HeaderDictionary(),
                        ContentType = "image/webp"
                    };

                    thumbUrl = await storageService.UploadFileAsync(thumbFile, Path.ChangeExtension(thumbKey, ".webp"));
                }
            }
        }
        catch (UnknownImageFormatException)
        {
            // For unsupported formats like HEIC, use original URL as thumbnail
            thumbUrl = originalUrl;
            logger.LogInformation("Unsupported image format for thumbnail generation, using original as thumbnail");
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
