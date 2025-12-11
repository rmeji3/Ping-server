using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Conquest.Services.Google;

public class GoogleAuthService
{
    private readonly string? _clientId;

    public GoogleAuthService(IConfiguration config)
    {
        // Ideally checking against your specific Client ID adds security
        _clientId = config["Google:ClientId"];
    }

    public async Task<GoogleJsonWebSignature.Payload> VerifyGoogleTokenAsync(string idToken)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings()
            {
                Audience = _clientId != null ? new[] { _clientId } : null
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return payload;
        }
        catch (InvalidJwtException ex)
        {
            Log.Warning(ex, "Invalid Google ID Token");
            throw new UnauthorizedAccessException("Invalid Google Token");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error validating Google Token");
            throw new Exception("Error validating external token");
        }
    }
}
