using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using Ping.Services.Storage;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Images;

public class ImageService(IStorageService storageService, HttpClient httpClient, ILogger<ImageService> logger) : IImageService
{
    private const int MaxThumbnailSize = 500;
    private const long MaxFileSize = 10 * 1024 * 1024; // 10MB

    // We must re-encode the original to bake in EXIF orientation, which is
    // lossy for JPEG/WebP. ImageSharp's default JPEG quality is only 75, which
    // visibly softens full-size photos (e.g. the ping detail hero). Re-encode
    // at a high quality so the stored original stays close to what was uploaded.
    private const int OriginalQuality = 92;

    public async Task<(string OriginalUrl, string ThumbnailUrl)> ProcessAndUploadImageAsync(IFormFile file, string folder, string userId)
    {
        // 1. Validation
        if (file.Length > MaxFileSize)
            throw new ArgumentException($"File size exceeds {MaxFileSize / 1024 / 1024}MB limit.");

        var ext = Path.GetExtension(file.FileName);
        var extLower = ext?.ToLowerInvariant();
        bool isLottie = extLower == ".json";
        bool isShader = extLower == ".sksl";

        if (isLottie || isShader)
        {
            if (folder != "stickers")
                throw new ArgumentException("Lottie and Shader files are only allowed for stickers.");

            var assetTimestamp = DateTime.UtcNow.Ticks;
            var assetKey = $"{folder}/{userId}/{assetTimestamp}_orig{extLower}";

            var contentType = isLottie ? "application/json" : "text/plain";
            using var fileStream = file.OpenReadStream();
            var uploadFile = new FormFile(fileStream, 0, file.Length, file.Name, file.FileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };

            var assetUrl = await storageService.UploadFileAsync(uploadFile, assetKey);
            logger.LogInformation("Uploaded non-image asset {OriginalKey} directly as Lottie/Shader", assetKey);
            return (assetUrl, assetUrl);
        }

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif", "image/heic", "image/heif", "image/svg+xml" };
        if (!allowedTypes.Contains(file.ContentType))
            throw new ArgumentException("Invalid file type. Only JPEG, PNG, WebP, GIF, HEIC, HEIF, and SVG are allowed.");

        // 2. Generate Keys
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
            // Pick a high-quality encoder for the lossy formats so re-encoding
            // doesn't degrade the photo; lossless/other formats keep their default.
            IImageEncoder originalEncoder = format.Name switch
            {
                "JPEG" => new JpegEncoder { Quality = OriginalQuality },
                "WEBP" => new WebpEncoder { Quality = OriginalQuality },
                _ => image.Configuration.ImageFormatsManager.GetEncoder(format),
            };
            using (var origOut = new MemoryStream())
            {
                await image.SaveAsync(origOut, originalEncoder);
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
