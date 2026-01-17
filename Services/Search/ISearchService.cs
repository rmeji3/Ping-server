using Ping.Dtos.Search;
using Ping.Dtos.Common;

namespace Ping.Services.Search;

public interface ISearchService
{
    Task<UnifiedSearchResultDto> UnifiedSearchAsync(UnifiedSearchFilterDto filter, string? userId);
    Task<List<SearchHistoryDto>> GetSearchHistoryAsync(string userId, int count = 20);
    Task AddToHistoryAsync(string userId, CreateSearchHistoryDto input);
    Task DeleteHistoryItemAsync(string userId, int historyId);
    Task ClearHistoryAsync(string userId);
}
