using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Moderation;

public class OpenAIModerationService(HttpClient http, IConfiguration config, ILogger<OpenAIModerationService> logger) : IModerationService
{
    private const string Endpoint = "https://api.openai.com/v1/moderations";

    public async Task<ModerationResult> CheckContentAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ModerationResult(false, null);

        var apiKey = config["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("OPENAI_API_KEY is missing. Moderation failing open.");
            return new ModerationResult(false, null);
        }

        try
        {
            var requestBody = new { input = text };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = content;

            var response = await http.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                logger.LogError("Moderation API failed: {StatusCode} - {Body}", response.StatusCode, errorBody);
                return new ModerationResult(false, null); // Fail open on API error
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ModerationResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Results != null && result.Results.Count > 0)
            {
                var first = result.Results[0];
                if (first.Flagged)
                {
                    // Find triggered categories
                    var reasons = new List<string>();
                    foreach (var prop in typeof(Categories).GetProperties())
                    {
                        if (prop.PropertyType == typeof(bool) && (bool)prop.GetValue(first.Categories)!)
                        {
                            reasons.Add(prop.Name);
                        }
                    }
                    var reasonStr = reasons.Count > 0 ? string.Join(", ", reasons) : "Content violation";
                    return new ModerationResult(true, reasonStr);
                }
            }

            return new ModerationResult(false, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Moderation service exception");
            return new ModerationResult(false, null); // Fail open
        }
    }

    // Response models
    private class ModerationResponse
    {
        public List<ModerationResultItem> Results { get; set; } = [];
    }

    private class ModerationResultItem
    {
        public bool Flagged { get; set; }
        public Categories Categories { get; set; } = new();
    }

    private class Categories
    {
        [JsonPropertyName("hate")] public bool Hate { get; set; }
        [JsonPropertyName("hate/threatening")] public bool HateThreatening { get; set; }
        [JsonPropertyName("harassment")] public bool Harassment { get; set; }
        [JsonPropertyName("harassment/threatening")] public bool HarassmentThreatening { get; set; }
        [JsonPropertyName("self-harm")] public bool SelfHarm { get; set; }
        [JsonPropertyName("self-harm/intent")] public bool SelfHarmIntent { get; set; }
        [JsonPropertyName("self-harm/instructions")] public bool SelfHarmInstructions { get; set; }
        [JsonPropertyName("sexual")] public bool Sexual { get; set; }
        [JsonPropertyName("sexual/minors")] public bool SexualMinors { get; set; }
        [JsonPropertyName("violence")] public bool Violence { get; set; }
        [JsonPropertyName("violence/graphic")] public bool ViolenceGraphic { get; set; }
    }
}
