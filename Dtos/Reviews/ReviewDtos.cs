using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Reviews;

public record ReviewDto(
    int Id,
    int Rating,
    string? Content,
    string UserId,
    string UserName,
    string? ProfilePictureUrl,
    string? ImageUrl,
    string? ThumbnailUrl,
    DateTime CreatedAt,
    int Likes,
    bool IsLiked,
    bool IsOwner,
    List<string> Tags,
    bool IsPingDeleted,
    List<string>? AdditionalImageUrls = null,
    // Optional place context. Populated by endpoints that key off a single
    // place (e.g. a user's reviews for one ping) so clients can show the place
    // name/address on share cards. Trailing + defaulted so the other call sites
    // that don't carry place context keep compiling unchanged.
    string? PingName = null,
    string? PingAddress = null
);

public record CreateReviewDto(
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    int Rating,
    [MaxLength(1000, ErrorMessage = "Content must be at most 1000 characters.")]
    string? Content,
    [Required, MaxLength(2048)]
    string? ImageUrl,
    [MaxLength(2048)]
    string? ThumbnailUrl,
    List<string>? Tags = null,
    List<string>? AdditionalImageUrls = null
);

public record UpdateReviewDto(
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    int? Rating,
    [MaxLength(1000, ErrorMessage = "Content must be at most 1000 characters.")]
    string? Content,
    [MaxLength(2048)]
    string? ImageUrl,
    [MaxLength(2048)]
    string? ThumbnailUrl,
    List<string>? Tags,
    List<string>? AdditionalImageUrls = null
);

public record ExploreReviewDto(
    int ReviewId,
    int PingActivityId,
    int PingId,
    string PingName,
    string PingAddress,
    string ActivityName,
    string? PingGenreName,
    double Latitude,
    double Longitude,
    int Rating,
    string? Content,
    string UserId,
    string UserName,
    string? ProfilePictureUrl,
    string ImageUrl,
    string ThumbnailUrl,
    DateTime CreatedAt,
    int Likes,
    bool IsLiked,
    bool IsOwner,
    List<string> Tags,
    bool IsPingDeleted,
    List<string>? AdditionalImageUrls = null
);

public class ExploreReviewsFilterDto
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? RadiusKm { get; set; }
    public string? SearchQuery { get; set; }
    public List<string>? Tags { get; set; }
    public List<int>? PingGenreIds { get; set; }
    public string Scope { get; set; } = "global";
    public int PageSize { get; set; } = 20;
    public int PageNumber { get; set; } = 1;
}
