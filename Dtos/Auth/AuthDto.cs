namespace Conquest.Dtos.Auth
{
    using Conquest.Models.AppUsers;
    using System.ComponentModel.DataAnnotations;

    public class JwtOptions
    {
        public string Key { get; set; } = default!;
        public string Issuer { get; set; } = default!;
        public string Audience { get; set; } = default!;
        public int AccessTokenMinutes { get; set; } = 60;
    }
    public record RegisterDto(
        [Required, EmailAddress] string Email,
        [Required, MinLength(6)] string Password,
        [Required, MaxLength(24)] string FirstName,
        [Required, MaxLength(24)] string LastName,
        [Required, MaxLength(24)] string UserName
    );

    public record LoginDto(
        [Required, EmailAddress] string Email,
        [Required] string Password
    );

    public record ForgotPasswordDto([Required, EmailAddress] string Email);

    public record ResetPasswordDto(
        [Required, EmailAddress] string Email,
        [Required] string Token,
        [Required, MinLength(6)] string NewPassword
    );

    public record ChangePasswordDto(
        [Required] string CurrentPassword,
        [Required, MinLength(6)] string NewPassword
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
        string? ProfileImageUrl
    );

    public record AuthResponse(string AccessToken, DateTime ExpiresUtc, UserDto User);
}
