namespace Ping.Models.Pings
{
    public class Favorited
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public int PingId { get; set; }

        // Navigation property
        public Ping Ping { get; set; } = null!;
    }
}
