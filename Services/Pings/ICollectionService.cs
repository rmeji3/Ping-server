using Ping.Dtos.Common;
using Ping.Dtos.Pings;

namespace Ping.Services.Pings
{
    public interface ICollectionService
    {
        Task<CollectionDto> CreateCollectionAsync(string userId, CreateCollectionDto dto);
        Task<List<CollectionDto>> GetMyCollectionsAsync(string userId);
        Task<List<CollectionDto>> GetUserPublicCollectionsAsync(string targetUserId, string? currentUserId);
        Task<CollectionDetailsDto> GetCollectionDetailsAsync(int collectionId, string? currentUserId);
        Task<CollectionDto> UpdateCollectionAsync(int collectionId, string userId, UpdateCollectionDto dto);
        Task DeleteCollectionAsync(int collectionId, string userId);
        Task AddPingToCollectionAsync(int collectionId, string userId, int pingId);
        Task RemovePingFromCollectionAsync(int collectionId, string userId, int pingId);
    }
}
