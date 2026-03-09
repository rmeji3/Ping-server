using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Pings
{
    /// <summary>Represents a user saving someone else's public collection to their own library.</summary>
    public class SavedCollection
    {
        public int Id { get; set; }

        /// <summary>The user doing the saving.</summary>
        [Required, MaxLength(100)]
        public string UserId { get; set; } = null!;

        /// <summary>The collection being saved.</summary>
        public int CollectionId { get; set; }
        public Collection Collection { get; set; } = null!;

        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }
}
