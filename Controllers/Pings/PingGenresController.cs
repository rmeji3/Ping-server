using Ping.Data.App;
using Ping.Models.Pings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;

namespace Ping.Controllers.Pings
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/ping-genres")]
    [Route("api/v{version:apiVersion}/ping-genres")]
    public class PingGenresController(AppDbContext db) : ControllerBase
    {
        // GET /api/ping-genres
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PingGenre>>> GetAll()
        {
            var genres = await db.PingGenres
                .OrderBy(g => g.Name)
                .ToListAsync();

            return Ok(genres);
        }
    }
}
