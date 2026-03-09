using Ping.Data.App;
using Ping.Dtos.Pings;
using Ping.Models.Pings;
using Ping.Services.Follows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ping.Utils;

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
                IsSystem = false, // User-created collections are never system collections
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
            // 1. Owned collections — always appear first
            var ownedCollections = await db.Collections
                .Where(c => c.UserId == userId)
                .Include(c => c.CollectionPings)
                    .ThenInclude(cp => cp.Ping)
                .OrderByDescending(c => c.CreatedUtc)
                .ToListAsync();

            var result = new List<CollectionDto>();
            foreach (var c in ownedCollections)
            {
                var dto = MapToDto(c, c.CollectionPings.Count, isOwner: true, isSaved: false);

                if (c.ImageUrl == null)
                {
                    var (img, thumb) = await GetDefaultCollectionThumbnailAsync(c.Id);
                    dto = dto with { ImageUrl = img, ThumbnailUrl = thumb };
                }

                result.Add(dto);
            }

            // 2. Saved collections from other users — appended after owned
            var saved = await db.SavedCollections
                .Where(sc => sc.UserId == userId)
                .Include(sc => sc.Collection)
                    .ThenInclude(c => c.CollectionPings)
                .OrderByDescending(sc => sc.SavedAt)
                .ToListAsync();

            foreach (var sc in saved)
            {
                var dto = MapToDto(sc.Collection, sc.Collection.CollectionPings.Count, isOwner: false, isSaved: true);

                if (sc.Collection.ImageUrl == null)
                {
                    var (img, thumb) = await GetDefaultCollectionThumbnailAsync(sc.Collection.Id);
                    dto = dto with { ImageUrl = img, ThumbnailUrl = thumb };
                }

                result.Add(dto);
            }

            return result;
        }

        public async Task<List<CollectionDto>> GetUserPublicCollectionsAsync(string targetUserId, string? currentUserId)
        {
            // If viewing someone else, only show public
            var collections = await db.Collections
                .Where(c => c.UserId == targetUserId && c.IsPublic)
                .Include(c => c.CollectionPings)
                    .ThenInclude(cp => cp.Ping)
                .OrderByDescending(c => c.CreatedUtc)
                .ToListAsync();

            // Pre-load all saved collection IDs for the current user in one query (avoid N+1)
            HashSet<int> savedIds = currentUserId != null
                ? (await db.SavedCollections
                    .Where(sc => sc.UserId == currentUserId)
                    .Select(sc => sc.CollectionId)
                    .ToListAsync()).ToHashSet()
                : new HashSet<int>();

            var result = new List<CollectionDto>();
            foreach (var c in collections)
            {
                var isOwner = currentUserId != null && c.UserId == currentUserId;
                var isSaved = savedIds.Contains(c.Id);
                var dto = MapToDto(c, c.CollectionPings.Count, isOwner, isSaved);

                if (c.ImageUrl == null)
                {
                    var (img, thumb) = await GetDefaultCollectionThumbnailAsync(c.Id);
                    dto = dto with { ImageUrl = img, ThumbnailUrl = thumb };
                }

                result.Add(dto);
            }
            return result;
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

            var isOwner = currentUserId != null && collection.UserId == currentUserId;
            var isSaved = currentUserId != null && await db.SavedCollections
                .AnyAsync(sc => sc.UserId == currentUserId && sc.CollectionId == collectionId);

            // If the user hasn't uploaded a custom image, derive the cover dynamically
            string? imageUrl = collection.ImageUrl;
            string? thumbnailUrl = collection.ThumbnailUrl;
            if (collection.ImageUrl == null)
            {
                var (img, thumb) = await GetDefaultCollectionThumbnailAsync(collection.Id);
                imageUrl = img;
                thumbnailUrl = thumb;
            }

            return new CollectionDetailsDto(
                collection.Id,
                collection.Name,
                collection.IsPublic,
                collection.CollectionPings.Count,
                imageUrl,
                thumbnailUrl,
                collection.CreatedUtc,
                pings,
                collection.IsSystem,
                isOwner,
                isSaved
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

        private static CollectionDto MapToDto(Collection c, int count, bool isOwner = false, bool isSaved = false)
        {
            return new CollectionDto(
                c.Id,
                c.Name,
                c.IsPublic,
                count,
                c.ImageUrl,
                c.ThumbnailUrl,
                c.CreatedUtc,
                c.IsSystem,
                isOwner,
                isSaved
            );
        }

        /// <summary>Save a public collection to the current user's saved library.</summary>
        public async Task SaveCollectionAsync(int collectionId, string userId)
        {
            var collection = await db.Collections.FindAsync(collectionId);
            if (collection == null) throw new KeyNotFoundException("Collection not found.");

            // Users cannot save their own collections
            if (collection.UserId == userId)
                throw new InvalidOperationException("You cannot save your own collection.");

            // Collection must be public to be saved
            if (!collection.IsPublic)
                throw new InvalidOperationException("You can only save public collections.");

            var already = await db.SavedCollections
                .AnyAsync(sc => sc.UserId == userId && sc.CollectionId == collectionId);

            if (!already)
            {
                db.SavedCollections.Add(new SavedCollection
                {
                    UserId = userId,
                    CollectionId = collectionId,
                    SavedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }

        /// <summary>Remove a previously saved collection from the user's library.</summary>
        public async Task UnsaveCollectionAsync(int collectionId, string userId)
        {
            var saved = await db.SavedCollections
                .FirstOrDefaultAsync(sc => sc.UserId == userId && sc.CollectionId == collectionId);

            if (saved != null)
            {
                db.SavedCollections.Remove(saved);
                await db.SaveChangesAsync();
            }
        }

        /// <summary>Get all collections the user has saved from other users.</summary>
        public async Task<List<CollectionDto>> GetSavedCollectionsAsync(string userId)
        {
            var saved = await db.SavedCollections
                .Where(sc => sc.UserId == userId)
                .Include(sc => sc.Collection)
                    .ThenInclude(c => c.CollectionPings)
                .OrderByDescending(sc => sc.SavedAt)
                .ToListAsync();

            var result = new List<CollectionDto>();
            foreach (var sc in saved)
            {
                var dto = MapToDto(sc.Collection, sc.Collection.CollectionPings.Count, isOwner: false, isSaved: true);

                if (sc.Collection.ImageUrl == null)
                {
                    var (img, thumb) = await GetDefaultCollectionThumbnailAsync(sc.Collection.Id);
                    dto = dto with { ImageUrl = img, ThumbnailUrl = thumb };
                }

                result.Add(dto);
            }
            return result;
        }

        /// <summary>
        /// Computes the default cover image for a collection when the user hasn't uploaded one:
        /// finds the most recently added ping in the collection, then returns the
        /// thumbnail of the most-liked review across all activities for that ping.
        /// Returns (null, null) if no reviews exist yet.
        /// </summary>
        private async Task<(string? ImageUrl, string? ThumbnailUrl)> GetDefaultCollectionThumbnailAsync(int collectionId)
        {
            // Get the most recently added ping in the collection
            var mostRecentPingId = await db.CollectionPings
                .Where(cp => cp.CollectionId == collectionId)
                .OrderByDescending(cp => cp.AddedUtc)
                .Select(cp => (int?)cp.PingId)
                .FirstOrDefaultAsync();

            if (mostRecentPingId == null)
                return (null, null);

            // Get the activity IDs for that ping
            var activityIds = await db.PingActivities
                .Where(a => a.PingId == mostRecentPingId)
                .Select(a => a.Id)
                .ToListAsync();

            if (!activityIds.Any())
                return (null, null);

            // Get the most-liked review's image URLs for those activities
            var topReview = await db.Reviews
                .Where(r => activityIds.Contains(r.PingActivityId))
                .OrderByDescending(r => r.Likes)
                .Select(r => new { r.ImageUrl, r.ThumbnailUrl })
                .FirstOrDefaultAsync();

            return (topReview?.ImageUrl, topReview?.ThumbnailUrl);
        }
    }
}
