namespace Ping.Utils;

public static class UrlUtils
{
    private static string GetPlaceholderUrl(string fileName)
    {
        var bucket = Environment.GetEnvironmentVariable("AWS__BucketName");
        var region = Environment.GetEnvironmentVariable("AWS__Region");

        if (!string.IsNullOrEmpty(bucket) && !string.IsNullOrEmpty(region))
        {
            return $"https://{bucket}.s3.{region}.amazonaws.com/placeholders/{fileName}";
        }

        return $"https://placehold.co/600x600?text={fileName.Replace(".png", "")}";
    }

    private static readonly string PlaceholderS3Url = GetPlaceholderUrl("no-image.png");
    public static readonly string ProfilePlaceholderS3Url = GetPlaceholderUrl("default-avatar.png");

    public static string SanitizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return PlaceholderS3Url;
        
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return PlaceholderS3Url;
        }
        
        return url;
    }

    public static string? SanitizeProfileUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null; // Or return ProfilePlaceholderS3Url
        
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return ProfilePlaceholderS3Url;
        }
        
        return url;
    }

    public static bool IsLocalPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
    }
}
