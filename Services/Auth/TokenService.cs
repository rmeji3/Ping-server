namespace Ping.Features.Auth
{
    using Ping.Data.Auth;
    using Ping.Dtos.Auth;
    using Ping.Models.Users;
    using Models.AppUsers;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Options;
    using Microsoft.IdentityModel.Tokens;
    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Security.Cryptography;
    using System.Text;

    public class TokenService(
        UserManager<AppUser> users,
        IOptions<JwtOptions> opts,
        AuthDbContext db) : ITokenService
    {
        private readonly JwtOptions _opts = opts.Value;

        /// <summary>
        /// Creates a new access + refresh token pair for the user.
        /// </summary>
        public async Task<AuthResponse> CreateAuthResponseAsync(AppUser user, string? deviceId = null)
        {
            var accessToken = await GenerateAccessTokenAsync(user);
            var (refreshTokenRaw, refreshExpires) = await CreateRefreshTokenAsync(user.Id, deviceId);

            var roles = await users.GetRolesAsync(user);
            var userDto = new UserDto(user.Id, user.Email ?? "", user.UserName!, user.ProfileImageUrl, roles.ToArray());

            return new AuthResponse(
                accessToken.Jwt,
                accessToken.ExpiresUtc,
                refreshTokenRaw,
                refreshExpires,
                userDto
            );
        }

        /// <summary>
        /// Exchanges a refresh token for a new access + refresh token pair.
        /// The old refresh token is revoked (rotation).
        /// </summary>
        public async Task<AuthResponse> RefreshAsync(string refreshToken, string? deviceId = null)
        {
            var hash = HashToken(refreshToken);

            var stored = await db.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.TokenHash == hash);

            if (stored is null || !stored.IsActive)
                throw new UnauthorizedAccessException("Invalid or expired refresh token.");

            // Revoke the old token (token rotation)
            stored.RevokedUtc = DateTime.UtcNow;
            
            // Issue a new pair
            var response = await CreateAuthResponseAsync(stored.User, deviceId ?? stored.DeviceId);
            await db.SaveChangesAsync();

            return response;
        }

        /// <summary>
        /// Revokes a single refresh token (e.g., on logout).
        /// </summary>
        public async Task RevokeRefreshTokenAsync(string refreshToken)
        {
            var hash = HashToken(refreshToken);

            var stored = await db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.TokenHash == hash);

            if (stored is not null && stored.IsActive)
            {
                stored.RevokedUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Revokes all refresh tokens for a user (e.g., password change, account compromise).
        /// </summary>
        public async Task RevokeAllUserTokensAsync(string userId)
        {
            var activeTokens = await db.RefreshTokens
                .Where(rt => rt.UserId == userId && rt.RevokedUtc == null && rt.ExpiresUtc > DateTime.UtcNow)
                .ToListAsync();

            foreach (var token in activeTokens)
                token.RevokedUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        // --- Private helpers ---

        private async Task<(string Jwt, DateTime ExpiresUtc)> GenerateAccessTokenAsync(AppUser user)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
            };

            var roles = await users.GetRolesAsync(user);
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Key));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);

            var token = new JwtSecurityToken(
                issuer: _opts.Issuer,
                audience: _opts.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: credentials
            );

            var jwt = new JwtSecurityTokenHandler().WriteToken(token);
            return (jwt, expires);
        }

        private async Task<(string RawToken, DateTime ExpiresUtc)> CreateRefreshTokenAsync(string userId, string? deviceId)
        {
            // Revoke any existing token for this user+device (one active per device)
            if (deviceId is not null)
            {
                var existing = await db.RefreshTokens
                    .Where(rt => rt.UserId == userId && rt.DeviceId == deviceId && rt.RevokedUtc == null)
                    .ToListAsync();

                foreach (var old in existing)
                    old.RevokedUtc = DateTime.UtcNow;
            }

            // Generate a cryptographically random 64-byte token
            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var expiresUtc = DateTime.UtcNow.AddDays(_opts.RefreshTokenDays);

            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                TokenHash = HashToken(rawToken),
                DeviceId = deviceId,
                ExpiresUtc = expiresUtc,
                CreatedUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
            return (rawToken, expiresUtc);
        }

        /// <summary>
        /// SHA-256 hash of the raw refresh token. We never store the raw value.
        /// </summary>
        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(bytes);
        }
    }
}

