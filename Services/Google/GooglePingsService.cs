using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Google;

public class GooglePingsService(HttpClient httpClient, IConfiguration config, ILogger<GooglePingsService> logger) : IPingNameService
{
    public async Task<string?> GetPingNameAsync(double lat, double lng)
    {
        var apiKey = config["Google:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Google API Key is missing.");
            return null;
        }

        try
        {
            var requestBody = new
            {
                maxResultCount = 1,
                locationRestriction = new
                {
                    circle = new
                    {
                        center = new
                        {
                            latitude = lat,
                            longitude = lng
                        },
                        radius = 50.0
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchNearby");
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            request.Headers.Add("X-Goog-FieldMask", "places.displayName");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                logger.LogError("Google Places API error: {StatusCode}, Body: {ErrorBody}", response.StatusCode, errorBody);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("places", out var places) && places.GetArrayLength() > 0)
            {
                var firstPlace = places[0];
                if (firstPlace.TryGetProperty("displayName", out var displayNameObj))
                {
                    if (displayNameObj.TryGetProperty("text", out var textProp))
                    {
                        var placeName = textProp.GetString();
                        logger.LogInformation("Google Places found POI: '{PlaceName}' for coordinates {Lat}, {Lng}", placeName, lat, lng);
                        return placeName;
                    }
                }
            }

            logger.LogInformation("Google Places found NO POI for coordinates {Lat}, {Lng}. Falling back to user name.", lat, lng);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling Google Places API");
            return null;
        }
    }
    public async Task<List<GooglePingInfo>> SearchPingsAsync(string query, double lat, double lng, double radiusKm)
    {
        var apiKey = config["Google:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Google API Key is missing.");
            return [];
        }

        try
        {
            // Calculate bounding box for locationRestriction (circle is not supported)
            // 1 degree lat is approx 111km
            var latDegrees = radiusKm / 111.0;
            // Adjust longitude for latitude (approximate)
            var lngDegrees = radiusKm / (111.0 * Math.Cos(lat * Math.PI / 180.0));

            var minLat = lat - latDegrees;
            var maxLat = lat + latDegrees;
            var minLng = lng - lngDegrees;
            var maxLng = lng + lngDegrees;

            var requestBody = new
            {
                textQuery = query,
                locationRestriction = new
                {
                    rectangle = new
                    {
                        low = new
                        {
                            latitude = minLat,
                            longitude = minLng
                        },
                        high = new
                        {
                            latitude = maxLat,
                            longitude = maxLng
                        }
                    }
                },
                maxResultCount = 5
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchText");
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            // Requesting name, address, location, and types
            request.Headers.Add("X-Goog-FieldMask", "places.displayName,places.formattedAddress,places.location,places.types");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                logger.LogError("Google Places Search API error: {StatusCode}, Body: {ErrorBody}", response.StatusCode, errorBody);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var results = new List<GooglePingInfo>();
            if (doc.RootElement.TryGetProperty("places", out var places) && places.GetArrayLength() > 0)
            {
                foreach (var place in places.EnumerateArray())
                {
                    string name = "";
                    if (place.TryGetProperty("displayName", out var displayNameObj) && 
                        displayNameObj.TryGetProperty("text", out var textProp))
                    {
                        name = textProp.GetString() ?? "";
                    }

                    string? address = null;
                    if (place.TryGetProperty("formattedAddress", out var addrProp))
                    {
                        address = addrProp.GetString();
                    }

                    double? pLat = null;
                    double? pLng = null;
                    if (place.TryGetProperty("location", out var locObj))
                    {
                        if (locObj.TryGetProperty("latitude", out var latProp)) pLat = latProp.GetDouble();
                        if (locObj.TryGetProperty("longitude", out var lngProp)) pLng = lngProp.GetDouble();
                    }

                    var types = new List<string>();
                    if (place.TryGetProperty("types", out var typesProp))
                    {
                        foreach (var typeElement in typesProp.EnumerateArray())
                        {
                            var tStr = typeElement.GetString();
                            if (!string.IsNullOrEmpty(tStr))
                            {
                                types.Add(tStr);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        results.Add(new GooglePingInfo(name, address, pLat, pLng, types));
                    }
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling Google Places Search API");
            return [];
        }
    }

    public async Task<GooglePingInfo?> GetGooglePlaceByIdAsync(string placeId)
    {
        var apiKey = config["Google:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Google API Key is missing.");
            return null;
        }

        try
        {
            // https://places.googleapis.com/v1/places/{placeId}
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://places.googleapis.com/v1/places/{placeId}");
            request.Headers.Add("X-Goog-Api-Key", apiKey);
            request.Headers.Add("X-Goog-FieldMask", "displayName,formattedAddress,location");

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                logger.LogError("Google Places ID Lookup error: {StatusCode}, Body: {ErrorBody}", response.StatusCode, errorBody);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            string name = "";
            if (doc.RootElement.TryGetProperty("displayName", out var displayNameObj) && 
                displayNameObj.TryGetProperty("text", out var textProp))
            {
                name = textProp.GetString() ?? "";
            }

            string? address = null;
            if (doc.RootElement.TryGetProperty("formattedAddress", out var addrProp))
            {
                address = addrProp.GetString();
            }

            double? pLat = null;
            double? pLng = null;
            if (doc.RootElement.TryGetProperty("location", out var locObj))
            {
                if (locObj.TryGetProperty("latitude", out var latProp)) pLat = latProp.GetDouble();
                if (locObj.TryGetProperty("longitude", out var lngProp)) pLng = lngProp.GetDouble();
            }

            return new GooglePingInfo(name, address, pLat, pLng);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling Google Places ID Lookup");
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetGooglePlaceTypesAsync(string placeId)
    {
        var apiKey = config["Google:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Google API Key is missing — cannot fetch place types.");
            return [];
        }

        try
        {
            // Request only the `types` field to minimise billing cost.
            var url = $"https://maps.googleapis.com/maps/api/place/details/json?place_id={Uri.EscapeDataString(placeId)}&fields=types&key={apiKey}&language=en";
            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                logger.LogError("Google Place Types fetch error: {StatusCode}, Body: {Error}", response.StatusCode, err);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("result", out var result))
                return [];

            if (!result.TryGetProperty("types", out var typesEl) || typesEl.ValueKind != System.Text.Json.JsonValueKind.Array)
                return [];

            var types = typesEl.EnumerateArray()
                .Select(t => t.GetString() ?? string.Empty)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            logger.LogInformation("Google Place types for {PlaceId}: [{Types}]", placeId, string.Join(", ", types));
            return types;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching Google Place types for {PlaceId}", placeId);
            return [];
        }
    }
}

