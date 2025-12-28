namespace Ping.Dtos.Auth
{
    using Models.AppUsers;
    using System.ComponentModel.DataAnnotations;

    public class JwtOptions
    {
        public string Key { get; init; } = default!;
        public string Issuer { get; init; } = default!;
        public string Audience { get; init; } = default!;
        public int AccessTokenMinutes { get; init; } = 60;
    }
    public record RegisterDto(
        [Required, EmailAddress] string Email,
        [Required, MinLength(6)] string Password,
        [Required, MaxLength(24)] string FirstName,
        [Required, MaxLength(24)] string LastName,
        [Required, MaxLength(24)] string UserName
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
        [Required, MaxLength(24), MinLength(3)] string NewUserName
    );

    public interface ITokenService
    {
        Task<AuthResponse> CreateAuthResponseAsync(AppUser user);
    }

    public record UserDto(
        string Id, 
        string Email, 
        string? DisplayName, 
        string FirstName, 
        string LastName,
        string? ProfileImageUrl,
        string[] Roles
    );

    public record AuthResponse(string AccessToken, DateTime ExpiresUtc, UserDto User);
}

