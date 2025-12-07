using System.ComponentModel.DataAnnotations;
using Conquest.Models.Activities;
using Conquest.Models.Reviews;

namespace Conquest.Models.Places
{
    public class Place
    {
        public int Id { get; set; }
        [MaxLength(200)]
        public required string Name { get; set; } = null!;
        [MaxLength(300)]
        public string? Address { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        [MaxLength(100)]
        public string OwnerUserId { get; set; } = null!;
        public PlaceVisibility Visibility { get; set; } = PlaceVisibility.Private;
        public PlaceType Type { get; set; } = PlaceType.Custom;
        public ICollection<PlaceActivity> PlaceActivities { get; set; } = [];
        public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        public int Favorites { get; set; } = 0;
        public ICollection<Favorited> FavoritedList { get; set; } = [];
    }
}
