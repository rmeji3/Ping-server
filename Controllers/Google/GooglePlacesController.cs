using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ping.Controllers.Google;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize] // Default to auth for proxy routes unless explicitly allowed.
public class GooglePlacesController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GooglePlacesController> _logger;

    public GooglePlacesController(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<GooglePlacesController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("autocomplete/json")]
    public async Task<IActionResult> Autocomplete([FromQuery] string input, [FromQuery] string? types, [FromQuery] string? location, [FromQuery] string? radius, [FromQuery] string? components)
    {

        // Enforce minimum input length
        if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < 3)
        {
            _logger.LogWarning("Autocomplete abuse: input too short from IP {IP}", HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Query parameter 'input' is required and must be at least 3 characters.");
        }

        // Enforce max radius
        if (!string.IsNullOrEmpty(radius) && int.TryParse(radius, out var r) && r > 50000)
        {
            _logger.LogWarning("Autocomplete abuse: radius too large ({Radius}) from IP {IP}", radius, HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Radius must not exceed 50000 meters.");
        }

        // Endpoint-level quota: simple in-memory/IP counter (for demo, use Redis in prod)
        // (Pseudo: increment a counter in Redis, block if exceeded)
        // Example: 1000 req/hour/IP
        // (Assume RateLimitMiddleware covers this in prod)

        var apiKey = _config["Google:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return StatusCode(500, "Google API Key not configured on server.");
        }
        if (apiKey == "dummy-google-api-key-for-local-dev")
        {
            return Content("{\"status\":\"OK\",\"predictions\":[],\"results\":[]}", "application/json");
        }

        var client = _httpClientFactory.CreateClient();
        // Always enforce establishment-only — never trust the client param for `types`.
        var url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?input={Uri.EscapeDataString(input)}&key={apiKey}&language=en&types=establishment";

        if (!string.IsNullOrEmpty(location)) url += $"&location={location}";
        if (!string.IsNullOrEmpty(radius)) url += $"&radius={radius}";
        if (!string.IsNullOrEmpty(components)) url += $"&components={components}";

        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            // Post-filter: strip any prediction that Google returned whose types
            // indicate a geocode/administrative result (city, region, country, etc.)
            // despite the types=establishment param — the legacy API isn't always strict.
            var filtered = FilterEstablishmentsOnly(content);
            return Content(filtered ?? content, "application/json");
        }

        _logger.LogWarning("Google legacy autocomplete failed, trying Places API v1 fallback. Status: {StatusCode}", response.StatusCode);

        var fallbackJson = await AutocompleteWithPlacesV1Async(client, apiKey, input, location, radius);
        if (fallbackJson is not null)
        {
            return Content(fallbackJson, "application/json");
        }

        _logger.LogError("Google Autocomplete Proxy Error: {StatusCode} - {Content}", response.StatusCode, content);
        return StatusCode((int)response.StatusCode, content);
    }

    /// <summary>
    /// Post-filters a Google Places Autocomplete JSON response to remove any prediction
    /// that is purely a geocode result (city, region, country, postal code, route, etc.)
    /// rather than an actual establishment/point-of-interest.
    /// </summary>
    private static string? FilterEstablishmentsOnly(string json)
    {
        // Types that indicate a non-establishment geographic result.
        // A prediction is kept only if its types[] contains at least one establishment-like value.
        var geocodeOnlyTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "locality", "sublocality", "sublocality_level_1",
            "administrative_area_level_1", "administrative_area_level_2",
            "administrative_area_level_3", "country", "postal_code",
            "route", "street_address", "intersection", "political",
            "neighborhood", "colloquial_area", "natural_feature",
            "continent", "geocode"
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("predictions", out var predictions) ||
                predictions.ValueKind != JsonValueKind.Array)
            {
                return null; // Nothing to filter, pass through.
            }

            var kept = new List<object>();
            foreach (var prediction in predictions.EnumerateArray())
            {
                bool isEstablishment = true;

                if (prediction.TryGetProperty("types", out var typesEl) &&
                    typesEl.ValueKind == JsonValueKind.Array)
                {
                    var typeList = typesEl.EnumerateArray()
                        .Select(t => t.GetString() ?? string.Empty)
                        .ToList();

                    // If every type on this result is a geocode-only type, discard it.
                    if (typeList.Count > 0 && typeList.All(t => geocodeOnlyTypes.Contains(t)))
                    {
                        isEstablishment = false;
                    }
                }

                if (isEstablishment)
                {
                    kept.Add(JsonSerializer.Deserialize<object>(prediction.GetRawText())!);
                }
            }

            var result = new
            {
                status = root.TryGetProperty("status", out var s) ? s.GetString() : "OK",
                predictions = kept
            };

            return JsonSerializer.Serialize(result);
        }
        catch
        {
            return null; // On any parse error, fall through to the raw response.
        }
    }

    [AllowAnonymous]
    [HttpGet("details/json")]
    public async Task<IActionResult> Details([FromQuery] string place_id, [FromQuery] string? fields)
    {

        if (string.IsNullOrWhiteSpace(place_id) || place_id.Trim().Length < 5)
        {
            _logger.LogWarning("Details abuse: invalid place_id from IP {IP}", HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Query parameter 'place_id' is required and must be at least 5 characters.");
        }

        var apiKey = _config["Google:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return StatusCode(500, "Google API Key not configured on server.");
        }
        if (apiKey == "dummy-google-api-key-for-local-dev")
        {
            return Content("{\"status\":\"OK\",\"result\":{}}", "application/json");
        }

        var client = _httpClientFactory.CreateClient();
        var url = $"https://maps.googleapis.com/maps/api/place/details/json?place_id={place_id}&key={apiKey}&language=en";

        if (!string.IsNullOrEmpty(fields))
        {
            url += $"&fields={fields}";
        }

        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return Content(content, "application/json");
        }

        _logger.LogWarning("Google legacy details failed, trying Places API v1 fallback. Status: {StatusCode}", response.StatusCode);

        var fallbackJson = await DetailsWithPlacesV1Async(client, apiKey, place_id);
        if (fallbackJson is not null)
        {
            return Content(fallbackJson, "application/json");
        }

        _logger.LogError("Google Details Proxy Error: {StatusCode} - {Content}", response.StatusCode, content);
        return StatusCode((int)response.StatusCode, content);
    }

    [AllowAnonymous]
    [HttpGet("geocode/json")]
    public async Task<IActionResult> Geocode([FromQuery] string? address, [FromQuery] string? latlng, [FromQuery] string? place_id)
    {

        if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(latlng) && string.IsNullOrWhiteSpace(place_id))
        {
            _logger.LogWarning("Geocode abuse: missing all params from IP {IP}", HttpContext.Connection.RemoteIpAddress);
            return BadRequest("One of 'address', 'latlng', or 'place_id' is required.");
        }

        // Enforce minimum input length for address
        if (!string.IsNullOrWhiteSpace(address) && address.Trim().Length < 3)
        {
            _logger.LogWarning("Geocode abuse: address too short from IP {IP}", HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Address must be at least 3 characters.");
        }

        var apiKey = _config["Google:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return StatusCode(500, "Google API Key not configured on server.");
        }
        if (apiKey == "dummy-google-api-key-for-local-dev")
        {
            return Content("{\"status\":\"OK\",\"results\":[]}", "application/json");
        }

        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(address))
        {
            queryParts.Add($"address={Uri.EscapeDataString(address)}");
        }

        if (!string.IsNullOrWhiteSpace(latlng))
        {
            queryParts.Add($"latlng={Uri.EscapeDataString(latlng)}");
        }

        if (!string.IsNullOrWhiteSpace(place_id))
        {
            queryParts.Add($"place_id={Uri.EscapeDataString(place_id)}");
        }

        queryParts.Add("language=en");
        queryParts.Add($"key={Uri.EscapeDataString(apiKey)}");

        var url = $"https://maps.googleapis.com/maps/api/geocode/json?{string.Join("&", queryParts)}";

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Google Geocode Proxy Error: {StatusCode} - {Content}", response.StatusCode, content);
            return StatusCode((int)response.StatusCode, content);
        }

        return Content(content, "application/json");
    }

    [AllowAnonymous]
    [HttpGet("nearbysearch/json")]
    public async Task<IActionResult> NearbySearch(
        [FromQuery] string location,
        [FromQuery] string? radius,
        [FromQuery] string? keyword,
        [FromQuery] string? type,
        [FromQuery] string? rankby,
        [FromQuery] string? pagetoken)
    {

        if (string.IsNullOrWhiteSpace(location))
        {
            _logger.LogWarning("NearbySearch abuse: missing location from IP {IP}", HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Query parameter 'location' is required.");
        }

        // Enforce max radius
        if (!string.IsNullOrEmpty(radius) && int.TryParse(radius, out var r) && r > 50000)
        {
            _logger.LogWarning("NearbySearch abuse: radius too large ({Radius}) from IP {IP}", radius, HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Radius must not exceed 50000 meters.");
        }

        // Enforce min keyword/type length if present
        if (!string.IsNullOrWhiteSpace(keyword) && keyword.Trim().Length < 2)
        {
            _logger.LogWarning("NearbySearch abuse: keyword too short from IP {IP}", HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Keyword must be at least 2 characters.");
        }
        if (!string.IsNullOrWhiteSpace(type) && type.Trim().Length < 2)
        {
            _logger.LogWarning("NearbySearch abuse: type too short from IP {IP}", HttpContext.Connection.RemoteIpAddress);
            return BadRequest("Type must be at least 2 characters.");
        }

        var apiKey = _config["Google:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return StatusCode(500, "Google API Key not configured on server.");
        }
        if (apiKey == "dummy-google-api-key-for-local-dev")
        {
            return Content("{\"status\":\"OK\",\"results\":[]}", "application/json");
        }

        if (string.Equals(rankby, "distance", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(keyword) && string.IsNullOrWhiteSpace(type))
        {
            return BadRequest("When rankby=distance, either 'keyword' or 'type' is required.");
        }

        // Always enforce type=establishment — ignore the client param to prevent
        // geographic-only results (cities, regions, countries) from leaking through.
        var queryParts = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}",
            "language=en",
            "type=establishment",
            $"key={Uri.EscapeDataString(apiKey)}"
        };

        if (!string.IsNullOrWhiteSpace(radius))
        {
            queryParts.Add($"radius={Uri.EscapeDataString(radius)}");
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            queryParts.Add($"keyword={Uri.EscapeDataString(keyword)}");
        }

        if (!string.IsNullOrWhiteSpace(rankby))
        {
            queryParts.Add($"rankby={Uri.EscapeDataString(rankby)}");
        }

        if (!string.IsNullOrWhiteSpace(pagetoken))
        {
            queryParts.Add($"pagetoken={Uri.EscapeDataString(pagetoken)}");
        }

        var url = $"https://maps.googleapis.com/maps/api/place/nearbysearch/json?{string.Join("&", queryParts)}";

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Google Nearby Search Proxy Error: {StatusCode} - {Content}", response.StatusCode, content);
            return StatusCode((int)response.StatusCode, content);
        }

        // Post-filter: Google ignores type=establishment when rankby=distance is used
        // with a keyword that matches a city/region name (e.g. "Chicago" returns locality,political).
        var filteredNearby = FilterNearbyEstablishmentsOnly(content);
        return Content(filteredNearby ?? content, "application/json");
    }

    /// <summary>
    /// Post-filters a Google Places NearbySearch JSON response to remove any result
    /// whose types[] are all geocode/administrative (city, region, country, etc.)
    /// rather than an actual establishment or point of interest.
    /// </summary>
    private static string? FilterNearbyEstablishmentsOnly(string json)
    {
        var geocodeOnlyTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "locality", "sublocality", "sublocality_level_1",
            "administrative_area_level_1", "administrative_area_level_2",
            "administrative_area_level_3", "country", "postal_code",
            "route", "street_address", "intersection", "political",
            "neighborhood", "colloquial_area", "natural_feature",
            "continent", "geocode"
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var kept = new List<object>();
            foreach (var result in results.EnumerateArray())
            {
                bool isEstablishment = true;

                if (result.TryGetProperty("types", out var typesEl) &&
                    typesEl.ValueKind == JsonValueKind.Array)
                {
                    var typeList = typesEl.EnumerateArray()
                        .Select(t => t.GetString() ?? string.Empty)
                        .ToList();

                    if (typeList.Count > 0 && typeList.All(t => geocodeOnlyTypes.Contains(t)))
                    {
                        isEstablishment = false;
                    }
                }

                if (isEstablishment)
                {
                    kept.Add(JsonSerializer.Deserialize<object>(result.GetRawText())!);
                }
            }

            var filtered = new
            {
                status = root.TryGetProperty("status", out var s) ? s.GetString() : "OK",
                results = kept
            };

            return JsonSerializer.Serialize(filtered);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> AutocompleteWithPlacesV1Async(HttpClient client, string apiKey, string input, string? location, string? radius)
    {
        var requestPayload = new PlacesAutocompleteRequest
        {
            Input = input,
            LanguageCode = "en",
            IncludedPrimaryTypes = ["establishment"]
        };

        if (TryParseLocation(location, out var latitude, out var longitude))
        {
            requestPayload.LocationBias = new LocationBias
            {
                Circle = new Circle
                {
                    Center = new LatLng
                    {
                        Latitude = latitude,
                        Longitude = longitude
                    },
                    Radius = TryParseRadiusMeters(radius)
                }
            };
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:autocomplete");
        request.Headers.Add("X-Goog-Api-Key", apiKey);
        request.Headers.Add("X-Goog-FieldMask", "suggestions.placePrediction.placeId,suggestions.placePrediction.text,suggestions.placePrediction.structuredFormat");
        request.Content = new StringContent(JsonSerializer.Serialize(requestPayload), System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Google Places v1 autocomplete fallback failed: {StatusCode} - {Content}", response.StatusCode, content);
            return null;
        }

        using var doc = JsonDocument.Parse(content);
        var predictions = new List<object>();

        if (doc.RootElement.TryGetProperty("suggestions", out var suggestions) && suggestions.ValueKind == JsonValueKind.Array)
        {
            foreach (var suggestion in suggestions.EnumerateArray())
            {
                if (!suggestion.TryGetProperty("placePrediction", out var placePrediction))
                {
                    continue;
                }

                var description = GetNestedString(placePrediction, "text", "text") ?? string.Empty;
                var placeId = GetNestedString(placePrediction, "placeId");
                var mainText = GetNestedString(placePrediction, "structuredFormat", "mainText", "text");
                var secondaryText = GetNestedString(placePrediction, "structuredFormat", "secondaryText", "text");

                predictions.Add(new
                {
                    description,
                    place_id = placeId,
                    structured_formatting = new
                    {
                        main_text = mainText ?? description,
                        secondary_text = secondaryText
                    }
                });
            }
        }

        var result = new
        {
            predictions,
            status = "OK"
        };

        return JsonSerializer.Serialize(result);
    }

    private async Task<string?> DetailsWithPlacesV1Async(HttpClient client, string apiKey, string placeId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://places.googleapis.com/v1/places/{Uri.EscapeDataString(placeId)}");
        request.Headers.Add("X-Goog-Api-Key", apiKey);
        request.Headers.Add("X-Goog-FieldMask", "id,displayName,formattedAddress,location");

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Google Places v1 details fallback failed: {StatusCode} - {Content}", response.StatusCode, content);
            return null;
        }

        using var doc = JsonDocument.Parse(content);

        var result = new
        {
            result = new
            {
                name = GetNestedString(doc.RootElement, "displayName", "text"),
                formatted_address = GetNestedString(doc.RootElement, "formattedAddress"),
                place_id = GetNestedString(doc.RootElement, "id"),
                geometry = new
                {
                    location = new
                    {
                        lat = GetNestedDouble(doc.RootElement, "location", "latitude"),
                        lng = GetNestedDouble(doc.RootElement, "location", "longitude")
                    }
                }
            },
            status = "OK"
        };

        return JsonSerializer.Serialize(result);
    }

    private static bool TryParseLocation(string? location, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;

        if (string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        var parts = location.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        return double.TryParse(parts[0], out latitude) && double.TryParse(parts[1], out longitude);
    }

    private static double TryParseRadiusMeters(string? radius)
    {
        if (double.TryParse(radius, out var radiusMeters) && radiusMeters > 0)
        {
            return radiusMeters;
        }

        return 5000;
    }

    private static string? GetNestedString(JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static double? GetNestedDouble(JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var value))
        {
            return value;
        }

        return null;
    }

    private sealed class PlacesAutocompleteRequest
    {
        [JsonPropertyName("input")]
        public required string Input { get; init; }

        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; init; } = "en";

        [JsonPropertyName("includedPrimaryTypes")]
        public List<string>? IncludedPrimaryTypes { get; init; }

        [JsonPropertyName("locationBias")]
        public LocationBias? LocationBias { get; set; }
    }

    private sealed class LocationBias
    {
        [JsonPropertyName("circle")]
        public required Circle Circle { get; init; }
    }

    private sealed class Circle
    {
        [JsonPropertyName("center")]
        public required LatLng Center { get; init; }

        [JsonPropertyName("radius")]
        public double Radius { get; init; }
    }

    private sealed class LatLng
    {
        [JsonPropertyName("latitude")]
        public double Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; init; }
    }
}
