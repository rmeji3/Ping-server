using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Serilog;

namespace Conquest.Services.Apple;

public class AppleAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly string? _clientId; // This is the Bundle ID (e.g. com.example.app)
    private const string AppleKeysUrl = "https://appleid.apple.com/auth/keys";
    private const string AppleIssuer = "https://appleid.apple.com";

    public AppleAuthService(IHttpClientFactory httpClientFactory, IMemoryCache cache, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _clientId = config["Apple:ClientId"];
    }

    public async Task<ApplePayload> VerifyAppleTokenAsync(string idToken)
    {
        try
        {
            // 1. Get Apple's Public Keys (JWKS)
            var keys = await GetApplePublicKeysAsync();

            // 2. Validate Token
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(idToken);
            var kid = token.Header.Kid;

            var matchingKey = keys.FirstOrDefault(k => k.KeyId == kid)
                ?? throw new SecurityTokenSignatureKeyNotFoundException("Apple Key ID not found.");

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = AppleIssuer,
                ValidateAudience = true,
                ValidAudience = _clientId,
                ValidateLifetime = true,
                IssuerSigningKey = matchingKey,
            };

            var principal = handler.ValidateToken(idToken, validationParameters, out var validatedToken);
            
            var email = principal.FindFirst(ClaimTypes.Email)?.Value 
                        ?? principal.FindFirst("email")?.Value;
            var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                      ?? principal.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(email)) throw new Exception("Email claim missing from Apple token.");

            return new ApplePayload(sub!, email);
        }
        catch (SecurityTokenValidationException ex)
        {
             Log.Warning(ex, "Apple Token Validation Failed");
             throw new UnauthorizedAccessException("Invalid Apple Token");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error validating Apple Token");
            throw new Exception("Error validating Apple token");
        }
    }

    private async Task<List<SecurityKey>> GetApplePublicKeysAsync()
    {
        return await _cache.GetOrCreateAsync("AppleJwks", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            
            var client = _httpClientFactory.CreateClient();
            var json = await client.GetStringAsync(AppleKeysUrl);
            var keySet = JsonConvert.DeserializeObject<JsonWebKeySet>(json);

            var keys = new List<SecurityKey>();
            if (keySet == null || keySet.Keys == null) return keys;

            foreach (var webKey in keySet.Keys)
            {
                // Convert JWK to SecurityKey
                var e = Base64UrlEncoder.DecodeBytes(webKey.E);
                var n = Base64UrlEncoder.DecodeBytes(webKey.N);

                var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = n,
                    Exponent = e
                });

                var securityKey = new RsaSecurityKey(rsa)
                {
                    KeyId = webKey.Kid
                };
                keys.Add(securityKey);
            }
            return keys;
        }) ?? new List<SecurityKey>();
    }
}

public record ApplePayload(string Sub, string Email);

// Helper classes for standard JWKS deserialization if not using a library
public class JsonWebKeySet
{
    [JsonProperty("keys")]
    public List<JsonWebKey> Keys { get; set; } = new();
}

public class JsonWebKey
{
    [JsonProperty("kty")]
    public string Kty { get; set; } = "";
    [JsonProperty("kid")]
    public string Kid { get; set; } = "";
    [JsonProperty("use")]
    public string Use { get; set; } = "";
    [JsonProperty("alg")]
    public string Alg { get; set; } = "";
    [JsonProperty("n")]
    public string N { get; set; } = "";
    [JsonProperty("e")]
    public string E { get; set; } = "";
}
