using Ping.Models.Pings;
using Ping.Dtos.Common;
using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Pings;

public record PingSearchFilterDto
{
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? RadiusKm { get; init; } = 5;
    public string? Query { get; init; }
    
    [MaxLength(10)]
    public List<string>? ActivityNames { get; init; }
    
    [MaxLength(10)]
    public List<string>? PingGenreNames { get; init; }
    
    [MaxLength(10)]
    public List<string>? Tags { get; init; }
    
    public PingVisibility? Visibility { get; init; }
    public PingType? Type { get; init; }
    
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
