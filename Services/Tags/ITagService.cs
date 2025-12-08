using Conquest.Dtos.Tags;

namespace Conquest.Services.Tags;

public interface ITagService
{
    Task<IEnumerable<TagDto>> GetPopularTagsAsync(int count);
    Task<IEnumerable<TagDto>> SearchTagsAsync(string query, int count);
    Task ApproveTagAsync(int id);
    Task BanTagAsync(int id);
    Task MergeTagAsync(int sourceId, int targetId);
}
