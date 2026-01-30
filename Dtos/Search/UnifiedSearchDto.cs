using Ping.Dtos.Common;
using Ping.Dtos.Profiles;
using Ping.Dtos.Pings;

using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Search;

public record UnifiedSearchFilterDto(
    [Required] string Query,
    // Unified result pagination
    int PageNumber = 1,
    int PageSize = 20,
    // Geospatial (Used for Pings and Events)
    double? Latitude = null,
    double? Longitude = null,
    double? RadiusKm = null,
    // Ping specific
    [MaxLength(10)] string[]? ActivityNames = null,
    [MaxLength(10)] string[]? PingGenreNames = null,
    [MaxLength(10)] string[]? Tags = null
);

public record UnifiedSearchResultDto(
    PaginatedResult<ProfileDto> Profiles,
    PaginatedResult<PingDetailsDto> Pings
);
