namespace Conquest.Models.Places
{
    public class Favorited
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public int PlaceId { get; set; }

        // Navigation property
        public Place Place { get; set; } = null!;
    }
}