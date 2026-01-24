using Ping.Models.Search;
using System.ComponentModel.DataAnnotations;

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
    [Required, MaxLength(100)] string Query,
    SearchType Type,
    [MaxLength(256)] string? TargetId = null,
    [MaxLength(2048)] string? ImageUrl = null
);
