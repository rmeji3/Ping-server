using Conquest.Data.App;
using Conquest.Dtos.Activities;
using Conquest.Models.Activities;
using Conquest.Models.Places;
using Conquest.Services.AI;
using Conquest.Services.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Activities;

public class ActivityService(
    AppDbContext db, 
    IModerationService moderationService, 
    ISemanticService semanticService,
    ILogger<ActivityService> logger) : IActivityService
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
        
        // Moderation check
        var mod = await moderationService.CheckContentAsync(name);
        if (mod.IsFlagged)
        {
            logger.LogWarning("Activity name flagged: {Name} - {Reason}", name, mod.Reason);
            throw new ArgumentException($"Activity name rejected: {mod.Reason}");
        }

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
        var existingActivities = await db.PlaceActivities
            .Where(pa => pa.PlaceId == dto.PlaceId)
            .ToListAsync();

        // 4a. Exact Match
        var exactMatch = existingActivities.FirstOrDefault(pa => pa.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
        {
            logger.LogInformation("CreateActivity: Exact match found for '{Name}'. Returning existing ID {Id}.", name, exactMatch.Id);
            // Return existing activity details
            return new ActivityDetailsDto(
                exactMatch.Id, exactMatch.PlaceId, exactMatch.Name, exactMatch.ActivityKindId, exactMatch.ActivityKind?.Name, exactMatch.CreatedUtc,
                WarningMessage: $"Activity '{name}' already exists here."
            );
        }

        // 4b. AI Semantic Match
        var existingNames = existingActivities.Select(x => x.Name).ToList();
        if (existingNames.Count > 0)
        {
            var semanticMatchName = await semanticService.FindDuplicateAsync(name, existingNames);
            if (semanticMatchName != null)
            {
                var match = existingActivities.First(x => x.Name == semanticMatchName);
                 logger.LogInformation("CreateActivity: Semantic duplicate found. '{New}' -> '{Existing}'. Returning ID {Id}.", name, match.Name, match.Id);
                 return new ActivityDetailsDto(
                    match.Id, match.PlaceId, match.Name, match.ActivityKindId, match.ActivityKind?.Name, match.CreatedUtc,
                    WarningMessage: $"Merged '{name}' into existing activity '{match.Name}'."
                );
            }
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
