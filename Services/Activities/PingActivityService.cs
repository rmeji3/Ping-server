using Ping.Data.App;
using Ping.Dtos.Activities;
using Ping.Models.Pings;
using Ping.Services.AI;
using Ping.Services.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Activities;

using Ping.Models.Pings;

public class PingActivityService(
    AppDbContext db, 
    IModerationService moderationService, 
    ISemanticService semanticService,
    ILogger<PingActivityService> logger) : IPingActivityService
{
    public async Task<PingActivityDetailsDto> CreatePingActivityAsync(CreatePingActivityDto dto)
    {
        // 1. Validate ping
        var ping = await db.Pings
            .Include(p => p.PingGenre)
            .FirstOrDefaultAsync(p => p.Id == dto.PingId);
            
        if (ping is null)
        {
            logger.LogWarning("CreatePingActivity failed: Ping {PingId} not found.", dto.PingId);
            throw new KeyNotFoundException("Ping not found");
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

        // 3. PingGenre is on Ping, not Activity. Validation not needed here unless we validate Ping's genre.

        // 4. Enforce uniqueness per ping (PingId + Name)
        var existingActivities = await db.PingActivities
            .Where(pa => pa.PingId == dto.PingId)
            .ToListAsync();

        // 4a. Exact Match
        var exactMatch = existingActivities.FirstOrDefault(pa => pa.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
        {
            logger.LogInformation("CreatePingActivity: Exact match found for '{Name}'. Returning existing ID {Id}.", name, exactMatch.Id);
            // Return existing activity details
            return new PingActivityDetailsDto(
                exactMatch.Id, exactMatch.PingId, exactMatch.Name, exactMatch.PingId, exactMatch.Ping.PingGenre?.Name, exactMatch.CreatedUtc,
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
                 logger.LogInformation("CreatePingActivity: Semantic duplicate found. '{New}' -> '{Existing}'. Returning ID {Id}.", name, match.Name, match.Id);
                 return new PingActivityDetailsDto(
                    match.Id, match.PingId, match.Name, match.PingId, match.Ping.PingGenre?.Name, match.CreatedUtc,
                    WarningMessage: $"Merged '{name}' into existing activity '{match.Name}'."
                );
            }
        }

        // 5. Create PingActivity
        var pa = new PingActivity
        {
            PingId = dto.PingId,
            // PingGenreId not needed on Activity
            Name = name,
            CreatedUtc = DateTime.UtcNow
        };

        db.PingActivities.Add(pa);
        await db.SaveChangesAsync();

        // 6. Map to DTO
        var result = new PingActivityDetailsDto(
            pa.Id,
            pa.PingId,
            pa.Name,
            ping.PingGenreId, // Use ping object
            ping.PingGenre?.Name, // Use ping object
            pa.CreatedUtc
        );

        logger.LogInformation("Activity created: {ActivityId} at Ping {PingId}", pa.Id, pa.PingId);

        return result;
    }

    public async Task DeletePingActivityAsAdminAsync(int id)
    {
        var pa = await db.PingActivities.FindAsync(id);
        if (pa != null)
        {
            db.PingActivities.Remove(pa);
            await db.SaveChangesAsync();
            logger.LogInformation("Activity deleted by Admin: {ActivityId}", id);
        }
    }
}

