using Conquest.Dtos.Activities;
using Microsoft.AspNetCore.Authorization;

namespace Conquest.Controllers.Places
{
    using System.Security.Claims;
    using Data.App;
    using Conquest.Dtos.Places;
    using Conquest.Models.Places;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PlacesController(AppDbContext db) : ControllerBase
    {
        // POST /api/places
        [HttpPost]
        public async Task<ActionResult<PlaceDetailsDto>> Create([FromBody] UpsertPlaceDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Place name is required.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
                return Unauthorized("You must be logged in to create a place.");

            // Daily rate limit per user (e.g. 10 per day)
            var today = DateTime.UtcNow.Date;

            var createdToday = await db.Places
                .CountAsync(p => p.OwnerUserId == userId && p.CreatedUtc >= today);

            if (createdToday >= 10)
            {
                return BadRequest(new
                {
                    error = "You’ve reached the daily limit for adding places."
                });
            }

            var place = new Place
            {
                Name = dto.Name.Trim(),
                Address = dto.Address.Trim(),
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                OwnerUserId = userId,
                IsPublic = dto.IsPublic,
                CreatedUtc = DateTime.UtcNow
            };

            db.Places.Add(place);
            await db.SaveChangesAsync();

            var result = new PlaceDetailsDto(
                place.Id,
                place.Name,
                place.Address ?? string.Empty,
                place.Latitude,
                place.Longitude,
                place.IsPublic,
                IsOwner: true,
                Activities: Array.Empty<ActivitySummaryDto>(),
                ActivityKinds: Array.Empty<string>()
            );

            return CreatedAtAction(nameof(GetById), new { id = place.Id }, result);
        }

        // GET /api/places/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PlaceDetailsDto>> GetById(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var p = await db.Places
                .Include(x => x.PlaceActivities)
                    .ThenInclude(pa => pa.ActivityKind)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (p is null) return NotFound();

            var isOwner = userId != null && p.OwnerUserId == userId;

            // Hide private places from non-owners
            if (!p.IsPublic && !isOwner)
                return NotFound();

            var activities = p.PlaceActivities
                .Select(a => new ActivitySummaryDto(
                    a.Id,
                    a.Name,
                    a.ActivityKindId,
                    a.ActivityKind?.Name
                ))
                .ToArray();

            var activityKindNames = p.PlaceActivities
                .Where(a => a.ActivityKind != null)
                .Select(a => a.ActivityKind!.Name)
                .Distinct()
                .ToArray();

            return Ok(new PlaceDetailsDto(
                p.Id,
                p.Name,
                p.Address ?? string.Empty,
                p.Latitude,
                p.Longitude,
                p.IsPublic,
                isOwner,
                activities,
                activityKindNames
            ));
        }

        // GET /api/places/nearby?lat=..&lng=..&radiusKm=5&activityName=soccer&activityKind=outdoor
        [HttpGet("nearby")]
        public async Task<ActionResult<IEnumerable<PlaceDetailsDto>>> Nearby(
            [FromQuery] double lat,
            [FromQuery] double lng,
            [FromQuery] double radiusKm = 5,
            [FromQuery] string? activityName = null,
            [FromQuery] string? activityKind = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var latDelta = radiusKm / 111.0;
            var lngDelta = radiusKm / (111.0 * Math.Cos(lat * Math.PI / 180.0));
            var minLat = lat - latDelta;
            var maxLat = lat + latDelta;
            var minLng = lng - lngDelta;
            var maxLng = lng + lngDelta;

            var q = db.Places
                .Where(p => p.Latitude >= minLat && p.Latitude <= maxLat &&
                            p.Longitude >= minLng && p.Longitude <= maxLng)
                // show public places + my private ones
                .Where(p => p.IsPublic || (userId != null && p.OwnerUserId == userId))
                .Include(p => p.PlaceActivities)
                    .ThenInclude(pa => pa.ActivityKind)
                .AsQueryable();

            // Filter by ACTIVITY NAME (PlaceActivity.Name)
            if (!string.IsNullOrWhiteSpace(activityName))
            {
                var an = activityName.Trim().ToLowerInvariant();
                q = q.Where(p =>
                    p.PlaceActivities.Any(a =>
                        a.Name.ToLower() == an));
            }

            // Filter by ACTIVITY KIND (ActivityKind.Name)
            if (!string.IsNullOrWhiteSpace(activityKind))
            {
                var ak = activityKind.Trim().ToLowerInvariant();
                q = q.Where(p =>
                    p.PlaceActivities.Any(a =>
                        a.ActivityKind != null &&
                        a.ActivityKind.Name.ToLower() == ak));
            }

            var list = await q
                .Select(p => new
                {
                    p,
                    DistanceKm = 6371.0 * 2.0 * Math.Asin(
                        Math.Sqrt(
                            Math.Pow(Math.Sin((p.Latitude - lat) * Math.PI / 180.0 / 2.0), 2) +
                            Math.Cos(lat * Math.PI / 180.0) * Math.Cos(p.Latitude * Math.PI / 180.0) *
                            Math.Pow(Math.Sin((p.Longitude - lng) * Math.PI / 180.0 / 2.0), 2)
                        )
                    )
                })
                .Where(x => x.DistanceKm <= radiusKm)
                .OrderBy(x => x.DistanceKm)
                .Take(100)
                .ToListAsync();

            var result = list.Select(x =>
            {
                var activityKindNames = x.p.PlaceActivities
                    .Where(a => a.ActivityKind != null)
                    .Select(a => a.ActivityKind!.Name)
                    .Distinct()
                    .ToArray();

                var isOwner = userId != null && x.p.OwnerUserId == userId;

                var activities = x.p.PlaceActivities
                    .Select(a => new ActivitySummaryDto(
                        a.Id,
                        a.Name,
                        a.ActivityKindId,
                        a.ActivityKind?.Name
                    ))
                    .ToArray();
                
                return new PlaceDetailsDto(
                    x.p.Id,
                    x.p.Name,
                    x.p.Address ?? string.Empty,
                    x.p.Latitude,
                    x.p.Longitude,
                    x.p.IsPublic,
                    isOwner,
                    activities,
                    activityKindNames
                );
            }).ToList();

            return Ok(result);
        }
    }
}
