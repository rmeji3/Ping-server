using Ping.Data.App;
using Ping.Dtos.Pings;
using Ping.Models.Pings;
using Ping.Services.Follows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Pings
{
    public class CollectionService(
        AppDbContext db,
        IPingService pingService,
        ILogger<CollectionService> logger) : ICollectionService
    {
        public async Task<CollectionDto> CreateCollectionAsync(string userId, CreateCollectionDto dto)
        {
            var existing = await db.Collections
                .FirstOrDefaultAsync(c => c.UserId == userId && c.Name.ToLower() == dto.Name.ToLower());
            
            if (existing != null)
            {
                // If "All" collection already exists (created by system), just return it
                if (existing.Name.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    return MapToDto(existing, existing.CollectionPings.Count);
                }

                throw new InvalidOperationException($"You already have a collection named '{dto.Name}'.");
            }

            var collection = new Collection
            {
                UserId = userId,
                Name = dto.Name,
                IsPublic = dto.IsPublic,
                ImageUrl = dto.ImageUrl,
                ThumbnailUrl = dto.ThumbnailUrl,
                CreatedUtc = DateTime.UtcNow
            };

            db.Collections.Add(collection);
            await db.SaveChangesAsync();

            logger.LogInformation("Collection {CollectionId} created for user {UserId}", collection.Id, userId);

            return MapToDto(collection, 0);
        }

        public async Task<List<CollectionDto>> GetMyCollectionsAsync(string userId)
        {
            var collections = await db.Collections
                .Where(c => c.UserId == userId)
                .Include(c => c.CollectionPings)
                    .ThenInclude(cp => cp.Ping)
                .OrderByDescending(c => c.CreatedUtc)
                .ToListAsync();

            return collections.Select(c => 
            {
                return MapToDto(c, c.CollectionPings.Count);
            }).ToList();
        }

        public async Task<List<CollectionDto>> GetUserPublicCollectionsAsync(string targetUserId, string? currentUserId)
        {
            // If viewing someone else, only show public
            // (Standard rule, though we could add "Friends Only" collections later if needed)
            var collections = await db.Collections
                .Where(c => c.UserId == targetUserId && c.IsPublic)
                .Include(c => c.CollectionPings)
                    .ThenInclude(cp => cp.Ping)
                .OrderByDescending(c => c.CreatedUtc)
                .ToListAsync();

            return collections.Select(c => 
            {
                return MapToDto(c, c.CollectionPings.Count);
            }).ToList();
        }

        public async Task<CollectionDetailsDto> GetCollectionDetailsAsync(int collectionId, string? currentUserId)
        {
            var collection = await db.Collections
                .Include(c => c.CollectionPings)
                    .ThenInclude(cp => cp.Ping)
                .FirstOrDefaultAsync(c => c.Id == collectionId);

            if (collection == null)
                throw new KeyNotFoundException("Collection not found.");

            // Privacy check
            if (!collection.IsPublic && collection.UserId != currentUserId)
            {
                // Note: We might allow friends to see private collections if they are tagged/shared?
                // For now, Private means OWNER only.
                throw new UnauthorizedAccessException("You do not have permission to view this collection.");
            }

            var pings = new List<PingDetailsDto>();
            foreach (var cp in collection.CollectionPings.OrderByDescending(x => x.AddedUtc))
            {
                var details = await pingService.GetPingByIdAsync(cp.PingId, currentUserId);
                if (details != null)
                {
                    pings.Add(details);
                }
            }

            return new CollectionDetailsDto(
                collection.Id,
                collection.Name,
                collection.IsPublic,
                collection.CollectionPings.Count,
                collection.ImageUrl,
                collection.ThumbnailUrl,
                collection.CreatedUtc,
                pings
            );
        }

        public async Task<CollectionDto> UpdateCollectionAsync(int collectionId, string userId, UpdateCollectionDto dto)
        {
            var collection = await db.Collections.FindAsync(collectionId);
            if (collection == null) throw new KeyNotFoundException("Collection not found.");
            if (collection.UserId != userId) throw new UnauthorizedAccessException("Not your collection.");

            if (dto.Name != null) 
            {
                // Check name uniqueness if changing name
                if (!collection.Name.Equals(dto.Name, StringComparison.OrdinalIgnoreCase))
                {
                     var existing = await db.Collections
                        .AnyAsync(c => c.UserId == userId && c.Name.ToLower() == dto.Name.ToLower() && c.Id != collectionId);
                     if (existing) throw new InvalidOperationException($"You already have a collection named '{dto.Name}'.");
                }
                collection.Name = dto.Name;
            }
            if (dto.IsPublic.HasValue) collection.IsPublic = dto.IsPublic.Value;

            if (dto.ImageUrl != null) collection.ImageUrl = dto.ImageUrl;
            if (dto.ThumbnailUrl != null) collection.ThumbnailUrl = dto.ThumbnailUrl;

            await db.SaveChangesAsync();
            
            var count = await db.CollectionPings.CountAsync(cp => cp.CollectionId == collectionId);

            return MapToDto(collection, count);
        }

        public async Task DeleteCollectionAsync(int collectionId, string userId)
        {
            var collection = await db.Collections.FindAsync(collectionId);
            if (collection == null) throw new KeyNotFoundException("Collection not found.");
            if (collection.UserId != userId) throw new UnauthorizedAccessException("Not your collection.");
            
            if (collection.Name == "All") throw new InvalidOperationException("You cannot delete the 'All' collection.");

            db.Collections.Remove(collection);
            await db.SaveChangesAsync();
        }

        public async Task AddPingToCollectionAsync(int collectionId, string userId, int pingId)
        {
            var collection = await db.Collections.FindAsync(collectionId);
            if (collection == null) throw new KeyNotFoundException("Collection not found.");
            if (collection.UserId != userId) throw new UnauthorizedAccessException("Not your collection.");

            var exists = await db.CollectionPings.AnyAsync(cp => cp.CollectionId == collectionId && cp.PingId == pingId);
            if (!exists)
            {
                var collectionPing = new CollectionPing
                {
                    CollectionId = collectionId,
                    PingId = pingId,
                    AddedUtc = DateTime.UtcNow
                };
                db.CollectionPings.Add(collectionPing);
                await db.SaveChangesAsync();
            }
            
            // Sync with Global Favorites ("All" collection)
            // This ensures logic: "Adding to a collection implies Favoriting"
            await pingService.AddFavoriteAsync(pingId, userId);
        }

        public async Task RemovePingFromCollectionAsync(int collectionId, string userId, int pingId)
        {
            var collection = await db.Collections.FindAsync(collectionId);
            if (collection == null) throw new KeyNotFoundException("Collection not found.");
            if (collection.UserId != userId) throw new UnauthorizedAccessException("Not your collection.");

            var cp = await db.CollectionPings.FirstOrDefaultAsync(x => x.CollectionId == collectionId && x.PingId == pingId);
            if (cp != null)
            {
                db.CollectionPings.Remove(cp);
                await db.SaveChangesAsync();
            }
        }

        private static CollectionDto MapToDto(Collection c, int count)
        {
            return new CollectionDto(
                c.Id,
                c.Name,
                c.IsPublic,
                count,
                c.ImageUrl,
                c.ThumbnailUrl,
                c.CreatedUtc
            );
        }
    }
}
