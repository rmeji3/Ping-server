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
        if (string.IsNullOrWhiteSpace(input))
        {
            return BadRequest("Query parameter 'input' is required.");
        }

        var apiKey = _config["Google:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return StatusCode(500, "Google API Key not configured on server.");
        }

        var client = _httpClientFactory.CreateClient();
        var url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?input={Uri.EscapeDataString(input)}&key={apiKey}&language=en";

        if (!string.IsNullOrEmpty(types)) url += $"&types={types}";
        if (!string.IsNullOrEmpty(location)) url += $"&location={location}";
        if (!string.IsNullOrEmpty(radius)) url += $"&radius={radius}";
        if (!string.IsNullOrEmpty(components)) url += $"&components={components}";

        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return Content(content, "application/json");
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

    [AllowAnonymous]
    [HttpGet("details/json")]
    public async Task<IActionResult> Details([FromQuery] string place_id, [FromQuery] string? fields)
    {
        if (string.IsNullOrWhiteSpace(place_id))
        {
            return BadRequest("Query parameter 'place_id' is required.");
        }

        var apiKey = _config["Google:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return StatusCode(500, "Google API Key not configured on server.");
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
            return BadRequest("One of 'address', 'latlng', or 'place_id' is required.");
        }

        var apiKey = _config["Google:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return StatusCode(500, "Google API Key not configured on server.");
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
            return BadRequest("Query parameter 'location' is required.");
        }

        var apiKey = _config["Google:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            return StatusCode(500, "Google API Key not configured on server.");
        }

        if (string.Equals(rankby, "distance", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(keyword) && string.IsNullOrWhiteSpace(type))
        {
            return BadRequest("When rankby=distance, either 'keyword' or 'type' is required.");
        }

        var queryParts = new List<string>
        {
            $"location={Uri.EscapeDataString(location)}",
            "language=en",
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

        if (!string.IsNullOrWhiteSpace(type))
        {
            queryParts.Add($"type={Uri.EscapeDataString(type)}");
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

        return Content(content, "application/json");
    }

    private async Task<string?> AutocompleteWithPlacesV1Async(HttpClient client, string apiKey, string input, string? location, string? radius)
    {
        var requestPayload = new PlacesAutocompleteRequest
        {
            Input = input,
            LanguageCode = "en"
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
