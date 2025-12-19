namespace Ping.Dtos.Recommendations;

public class RecommendationDto
{
    public required string Name { get; set; }
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Source { get; set; } = "Local"; // "Local" or "Google"
    public int? LocalPingId { get; set; } // ID if it's a local ping
}

