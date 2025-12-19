using Ping.Data.App;
using Ping.Models.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;

namespace Ping.Controllers.Events
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/event-genres")]
    [Route("api/v{version:apiVersion}/event-genres")]
    public class EventGenresController(AppDbContext db) : ControllerBase
    {
        // GET /api/event-genres
        [HttpGet]
        public async Task<ActionResult<IEnumerable<EventGenre>>> GetAll()
        {
            var genres = await db.EventGenres
                .OrderBy(g => g.Name)
                .ToListAsync();

            return Ok(genres);
        }
    }
}
