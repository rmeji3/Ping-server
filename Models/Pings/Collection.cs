using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Pings
{
    public class Collection
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string UserId { get; set; } = null!;
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;
        
        public bool IsPublic { get; set; } = false;
        
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<CollectionPing> CollectionPings { get; set; } = new List<CollectionPing>();
    }
}
