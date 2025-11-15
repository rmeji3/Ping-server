namespace Conquest.Features.Auth
{
    using Conquest.Dtos.Auth;
    using Conquest.Models.AppUsers;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.Extensions.Options;
    using Microsoft.IdentityModel.Tokens;
    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;

    public class TokenService : ITokenService
    {
        private readonly UserManager<AppUser> _users;
        private readonly JwtOptions _opts;

        public TokenService(UserManager<AppUser> users, IOptions<JwtOptions> opts)
        {
            _users = users;
            _opts = opts.Value;
        }

        public async Task<AuthResponse> CreateAuthResponseAsync(AppUser user)
        {
            var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
        };

            var roles = await _users.GetRolesAsync(user);
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);

            var token = new JwtSecurityToken(
                issuer: _opts.Issuer,
                audience: _opts.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: creds
            );

            var jwt = new JwtSecurityTokenHandler().WriteToken(token);
            var userDto = new UserDto(user.Id, user.Email ?? "", user.UserName!, user.FirstName!, user.LastName!, user.ProfileImageUrl);
            return new AuthResponse(jwt, expires, userDto);
        }
    }
}
