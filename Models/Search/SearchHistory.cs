using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ping.Models.Search;

[Table("SearchHistory")]
public class SearchHistory
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Query { get; set; } = string.Empty;

    public SearchType Type { get; set; }

    /// <summary>
    /// Optional ID of the entity (User ID or Ping ID) if a specific result was selected.
    /// </summary>
    [MaxLength(100)]
    public string? TargetId { get; set; }
    
    /// <summary>
    /// Optional image URL to display in history (e.g. User profile pic or Ping image)
    /// </summary>
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
