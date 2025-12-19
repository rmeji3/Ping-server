using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Pings
{
    public class CollectionPing
    {
        public int CollectionId { get; set; }
        public Collection Collection { get; set; } = null!;
        
        public int PingId { get; set; }
        public Ping Ping { get; set; } = null!;

        public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    }
}
