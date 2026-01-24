using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Events;

public record EventFilterDto
{
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public int? GenreId { get; init; }
    
    // Geospatial
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? RadiusKm { get; init; }
    
    [MaxLength(100)]
    public string? Query { get; init; }
}
