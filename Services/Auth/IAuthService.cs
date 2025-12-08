using Conquest.Dtos.Auth;

namespace Conquest.Services.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterDto dto);
    Task<AuthResponse> LoginAsync(LoginDto dto);
    Task<UserDto> GetCurrentUserAsync(string userId);
    Task<object> ForgotPasswordAsync(ForgotPasswordDto dto, string scheme, string host);
    Task<string> ResetPasswordAsync(ResetPasswordDto dto);
    Task<string> ChangePasswordAsync(string userId, ChangePasswordDto dto);
    Task MakeAdminAsync(string email);
}
