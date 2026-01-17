using Ping.Models.Search;

namespace Ping.Dtos.Search;

public record SearchHistoryDto(
    int Id,
    string Query,
    SearchType Type,
    string? TargetId,
    string? ImageUrl,
    DateTime CreatedAt
);

public record CreateSearchHistoryDto(
    string Query,
    SearchType Type,
    string? TargetId = null,
    string? ImageUrl = null
);
