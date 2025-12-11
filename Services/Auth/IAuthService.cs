using Conquest.Dtos.Auth;

namespace Conquest.Services.Auth;

public interface IAuthService
{
    Task<object> RegisterAsync(RegisterDto dto);
    Task<AuthResponse> LoginAsync(LoginDto dto);
    Task<AuthResponse> VerifyEmailAsync(VerifyEmailDto dto);
    Task ResendVerificationEmailAsync(string email);
    Task<UserDto> GetCurrentUserAsync(string userId);
    Task<object> ForgotPasswordAsync(ForgotPasswordDto dto, string scheme, string host);
    Task<string> ResetPasswordAsync(ResetPasswordDto dto);
    Task<string> ChangePasswordAsync(string userId, ChangePasswordDto dto);
    Task MakeAdminAsync(string email);
    Task DeleteAccountAsync(string userId);
}
