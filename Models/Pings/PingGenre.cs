using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Pings
{
    public class PingGenre
    {
        public int Id { get; set; }
        [MaxLength(200)]
        public required string Name { get; set; }

        public List<Ping> Pings { get; set; } = [];
    }
}
