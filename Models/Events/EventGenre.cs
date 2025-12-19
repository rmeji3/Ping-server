using System.ComponentModel.DataAnnotations;
using Ping.Models.Events;

namespace Ping.Models.Events
{
    public class EventGenre
    {
        public int Id { get; set; }
        [MaxLength(200)]
        public required string Name { get; set; } // "sports, music, etc

        // nav
        public List<Event> Events { get; set; } = [];
    }

}

