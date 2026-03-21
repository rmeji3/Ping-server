namespace Ping.Dtos.Auth
{
    using Models.AppUsers;
    using System.ComponentModel.DataAnnotations;

    public class JwtOptions
    {
        public string Key { get; init; } = default!;
        public string Issuer { get; init; } = default!;
        public string Audience { get; init; } = default!;
        public int AccessTokenMinutes { get; init; } = 30;
        public int RefreshTokenDays { get; init; } = 30;
    }
    public record RegisterDto(
        [Required, EmailAddress] string Email,
        [Required, MinLength(6)] string Password,
        [Required, MaxLength(24)] string FirstName,
        [Required, MaxLength(24)] string LastName,
        [Required, MaxLength(24), RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username can only contain letters, numbers, and underscores.")] string UserName
    );

    public record LoginDto(
        [Required] string UserNameOrEmail,
        [Required] string Password
    );

    public record ForgotPasswordDto([Required, EmailAddress] string Email);
    
    public record VerifyEmailDto(
        [Required, EmailAddress] string Email,
        [Required] string Code
    );

    public record ResendVerificationDto([Required, EmailAddress] string Email);

    public record ResetPasswordDto(
        [Required, EmailAddress] string Email,
        [Required] string Code,
        [Required, MinLength(6)] string NewPassword
    );

    public record ChangePasswordDto(
        [Required] string CurrentPassword,
        [Required, MinLength(6)] string NewPassword
    );

    public record ChangeUsernameDto(
        [Required, MaxLength(24), MinLength(3), RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username can only contain letters, numbers, and underscores.")] string NewUserName
    );

    public interface ITokenService
    {
        Task<AuthResponse> CreateAuthResponseAsync(AppUser user, string? deviceId = null);
        Task<AuthResponse> RefreshAsync(string refreshToken, string? deviceId = null);
        Task RevokeRefreshTokenAsync(string refreshToken);
        Task RevokeAllUserTokensAsync(string userId);
    }

    public record UserDto(
        string Id, 
        string Email, 
        string? DisplayName, 
        string? ProfileImageUrl,
        string[] Roles
    );

    // Refresh token request from the client
    public record RefreshTokenRequest(
        [Required] string RefreshToken,
        string? DeviceId
    );

    public record AuthResponse(
        string AccessToken,
        DateTime ExpiresUtc,
        string RefreshToken,
        DateTime RefreshTokenExpiresUtc,
        UserDto User
    );
}

