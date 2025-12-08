using Conquest.Dtos.Auth;
using Conquest.Models.AppUsers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Conquest.Services.Auth;

public class AuthService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    ITokenService tokens,
    IHostEnvironment env,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterDto dto)
    {
        var user = new AppUser { 
            Email = dto.Email,
            UserName = dto.UserName,
            FirstName = dto.FirstName,
            LastName = dto.LastName
        };
        
        var normalized = dto.UserName.ToUpper();
        
        var existing = await users.Users
            .FirstOrDefaultAsync(u => u.NormalizedUserName == normalized);

        if (existing != null)
        {
            logger.LogWarning("Registration failed: Username '{UserName}' already taken.", dto.UserName);
            throw new InvalidOperationException("Username is already taken.");
        }

        var result = await users.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            logger.LogWarning("Registration failed for '{UserName}': {Errors}", dto.UserName, errors);
            throw new ArgumentException(errors);
        }

        logger.LogInformation("User registered: {UserId} ({UserName})", user.Id, user.UserName);
        return await tokens.CreateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email);
        if (user is null)
        {
            logger.LogWarning("Login failed: User '{Email}' not found.", dto.Email);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        var result = await signIn.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning("Login failed: Invalid password for '{Email}'.", dto.Email);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        logger.LogInformation("User logged in: {UserId}", user.Id);
        return await tokens.CreateAuthResponseAsync(user);
    }

    public async Task<UserDto> GetCurrentUserAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) throw new KeyNotFoundException("User not found.");

        return new UserDto(user.Id, user.Email ?? "", user.UserName!, user.FirstName, user.LastName, user.ProfileImageUrl);
    }

    public async Task<object> ForgotPasswordAsync(ForgotPasswordDto dto, string scheme, string host)
    {
        var user = await users.FindByEmailAsync(dto.Email);
        // Always return success to avoid account enumeration
        if (user == null)
        {
            logger.LogInformation("Forgot password requested for non-existent email: {Email}", dto.Email);
            return new { message = "If the account exists, a reset link has been sent." };
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        // Build a link your mobile/web client can handle (adjust to your scheme/route)
        var link = $"{scheme}://{host}/reset-password?email={Uri.EscapeDataString(dto.Email)}&token={encoded}";

        // For development: return token so you can test without email.
        if (env.IsDevelopment())
        {
            return new
            {
                message = "Dev-only: use this token to call /auth/password/reset",
                token = encoded,
                link
            };
        }

        // In production, log that we would send an email
        logger.LogInformation("Password reset link generated for {Email}. Link: {Link}", dto.Email, link);

        return new { message = "If the account exists, a reset link has been sent." };
    }

    public async Task<string> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email);
        // Always return success to avoid account enumeration
        if (user == null) return "Password has been reset if the account exists.";

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token));
        }
        catch
        {
            throw new ArgumentException("Invalid token format.");
        }

        var result = await users.ResetPasswordAsync(user, decodedToken, dto.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new ArgumentException(errors);
        }

        logger.LogInformation("Password reset successfully for {UserId}", user.Id);
        return "Password reset successful.";
    }

    public async Task<string> ChangePasswordAsync(string userId, ChangePasswordDto dto)
    {
        var user = await users.FindByIdAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found.");

        var result = await users.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new ArgumentException(errors);
        }

        logger.LogInformation("Password changed for {UserId}", user.Id);
        return "Password changed.";
    }
    public async Task MakeAdminAsync(string email)
    {
        // Development only safety check could go here or in controller
        var user = await users.FindByEmailAsync(email);
        if (user == null) throw new KeyNotFoundException($"User with email {email} not found.");

        if (!await users.IsInRoleAsync(user, "Admin"))
        {
            await users.AddToRoleAsync(user, "Admin");
            logger.LogInformation("User {Email} promoted to Admin.", email);
        }
        else
        {
            logger.LogInformation("User {Email} is already Admin.", email);
        }
    }
}
