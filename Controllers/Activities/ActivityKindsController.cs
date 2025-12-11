using Conquest.Data.App;
using Conquest.Dtos.Activities;
using Conquest.Models.Activities;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;

namespace Conquest.Controllers.ActivityKinds
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/activity-kinds")]
    [Route("api/v{version:apiVersion}/activity-kinds")]
    public class ActivityKindsController(AppDbContext db) : ControllerBase
    {
        // GET /api/activity-kinds
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ActivityKindDto>>> GetAll()
        {
            var kinds = await db.ActivityKinds
                .OrderBy(k => k.Name)
                .Select(k => new ActivityKindDto(k.Id, k.Name))
                .ToListAsync();

            return Ok(kinds);
        }

        // POST /api/activity-kinds
        [HttpPost]
        public async Task<ActionResult<ActivityKindDto>> Create([FromBody] CreateActivityKindDto dto)
        {
            var normalized = dto.Name.Trim();

            // Prevent duplicates
            var exists = await db.ActivityKinds
                .AnyAsync(k => k.Name.ToLower() == normalized.ToLower());

            if (exists)
                return Conflict(new { error = "Activity kind already exists." });

            var kind = new ActivityKind
            {
                Name = normalized
            };

            db.ActivityKinds.Add(kind);
            await db.SaveChangesAsync();

            var result = new ActivityKindDto(kind.Id, kind.Name);

            return CreatedAtAction(nameof(GetAll), new { id = kind.Id }, result);
        }

        // DELETE /api/activity-kinds/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var kind = await db.ActivityKinds.FindAsync(id);
            if (kind is null)
                return NotFound();

            db.ActivityKinds.Remove(kind);
            await db.SaveChangesAsync();

            return NoContent();
        }
    }
}