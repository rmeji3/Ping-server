using Conquest.Data.App;
using Conquest.Dtos.Activities;
using Conquest.Models.Activities;
using Conquest.Models.Places;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Activities;

public class ActivityService(AppDbContext db, ILogger<ActivityService> logger) : IActivityService
{
    public async Task<ActivityDetailsDto> CreateActivityAsync(CreateActivityDto dto)
    {
        // 1. Validate place
        var place = await db.Places.FindAsync(dto.PlaceId);
        if (place is null)
        {
            logger.LogWarning("CreateActivity failed: Place {PlaceId} not found.", dto.PlaceId);
            throw new KeyNotFoundException("Place not found");
        }

        // 2. Normalize name
        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Activity name is required.");

        // 3. Optional: validate ActivityKind if provided
        ActivityKind? kind = null;
        if (dto.ActivityKindId is int kindId)
        {
            kind = await db.ActivityKinds.FindAsync(kindId);
            if (kind is null)
            {
                logger.LogWarning("CreateActivity failed: Invalid ActivityKindId {KindId}.", dto.ActivityKindId);
                throw new ArgumentException("Invalid activity kind.");
            }
        }

        // 4. Enforce uniqueness per place (PlaceId + Name)
        var exists = await db.PlaceActivities
            .AnyAsync(pa => pa.PlaceId == dto.PlaceId && pa.Name == name);

        if (exists)
        {
            logger.LogWarning("CreateActivity failed: Activity '{Name}' already exists at Place {PlaceId}.", name, dto.PlaceId);
            throw new InvalidOperationException("An activity with that name already exists at this place.");
        }

        // 5. Create PlaceActivity
        var pa = new PlaceActivity
        {
            PlaceId = dto.PlaceId,
            ActivityKindId = dto.ActivityKindId,
            Name = name,
            CreatedUtc = DateTime.UtcNow
        };

        db.PlaceActivities.Add(pa);
        await db.SaveChangesAsync();

        // 6. Map to DTO
        var result = new ActivityDetailsDto(
            pa.Id,
            pa.PlaceId,
            pa.Name,
            pa.ActivityKindId,
            kind?.Name,
            pa.CreatedUtc
        );

        logger.LogInformation("Activity created: {ActivityId} at Place {PlaceId}", pa.Id, pa.PlaceId);

        return result;
    }
}
