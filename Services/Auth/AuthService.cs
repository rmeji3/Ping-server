using Conquest.Dtos.Auth;
using Conquest.Models.AppUsers;
using Conquest.Models.Analytics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using Conquest.Services.Moderation;

namespace Conquest.Services.Auth;

public class AuthService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    ITokenService tokens,
    Conquest.Data.Auth.AuthDbContext db,
    IHostEnvironment env,
    ILogger<AuthService> logger,
    IModerationService moderationService,
    Conquest.Services.Email.IEmailService emailService,
    Microsoft.Extensions.Configuration.IConfiguration config,
    Conquest.Services.Redis.IRedisService redis) : IAuthService
{
    public async Task<object> RegisterAsync(RegisterDto dto)
    {
        var user = new AppUser { 
            Email = dto.Email,
            UserName = dto.UserName,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            EmailConfirmed = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        
        var normalized = dto.UserName.ToUpper();
        // moderation check for username
        var moderationResult = await moderationService.CheckContentAsync(dto.UserName);
        if (moderationResult.IsFlagged)
        {
            logger.LogWarning("Registration failed: Username '{UserName}' is flagged.", dto.UserName);
            throw new ArgumentException($"Username is flagged: {moderationResult.Reason}");
        }

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

        logger.LogInformation("User registered: {UserId} ({UserName}). Sending verification email.", user.Id, user.UserName);

        // Generate verification code (comment out for testing)
        await SendVerificationCodeAsync(user);

        return new { message = "Registration successful. Welcome!" };
    }

    public async Task<AuthResponse> LoginAsync(LoginDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email);
        if (user is null)
        {
            logger.LogWarning("Login failed: User '{Email}' not found.", dto.Email);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        if (user.IsBanned)
        {
             logger.LogWarning("Login failed: User '{Email}' is banned.", dto.Email);
             var msg = string.IsNullOrWhiteSpace(user.BanReason) ? "Your account has been banned." : $"Your account has been banned: {user.BanReason}";
             throw new UnauthorizedAccessException(msg);
        }
        // comment out for testing
        if (!await users.IsEmailConfirmedAsync(user))
        {
            logger.LogWarning("Login failed: User '{Email}' not confirmed.", dto.Email);
            throw new UnauthorizedAccessException("Please verify your email address.");
        }

        var result = await signIn.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning("Login failed: Invalid password for '{Email}'.", dto.Email);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // Analytics Tracking
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.DateTime);

        user.LastLoginUtc = now;
        // Identity tracks changes to user via UserManager or we can attach to context,
        // but since we have DB context, let's use it for the activity log.
        // Note: UserManager "Find" methods attach the user to the context it uses.
        // Assuming AuthDbContext is the backing store for UserManager.
        
        await users.UpdateAsync(user); // Persist LastLoginUtc

        // Update/Create Daily Log
        var log = await db.UserActivityLogs
            .FirstOrDefaultAsync(l => l.UserId == user.Id && l.Date == today);

        if (log == null)
        {
            log = new UserActivityLog
            {
                UserId = user.Id,
                Date = today,
                LoginCount = 1,
                LastActivityUtc = now
            };
            db.UserActivityLogs.Add(log);
        }
        else
        {
            log.LoginCount++;
            log.LastActivityUtc = now;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("User logged in: {UserId}", user.Id);
        return await tokens.CreateAuthResponseAsync(user);
    }

    public async Task<UserDto> GetCurrentUserAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) throw new KeyNotFoundException("User not found.");

        var roles = await users.GetRolesAsync(user);
        return new UserDto(user.Id, user.Email ?? "", user.UserName!, user.FirstName, user.LastName, user.ProfileImageUrl, roles.ToArray());
    }

    public async Task<object> ForgotPasswordAsync(ForgotPasswordDto dto, string scheme, string host)
    {
        var user = await users.FindByEmailAsync(dto.Email);
        // Always return success to avoid account enumeration
        if (user == null)
        {
            logger.LogInformation("Forgot password requested for non-existent email: {Email}", dto.Email);
            return new { message = "If the account exists, a reset code has been sent." };
        }

        await CheckEmailRateLimitAsync(dto.Email);

        // Generate 6-digit code
        var code = Random.Shared.Next(100000, 999999).ToString();
        var redisKey = $"password_reset:{dto.Email}";
        
        // Store in Redis for 15 minutes
        await redis.SetAsync(redisKey, code, TimeSpan.FromMinutes(15));
        
        // Send email
        await emailService.SendEmailAsync(
            dto.Email, 
            "Reset your Conquest password", 
            $"Your password reset code is: <b>{code}</b>. It expires in 15 minutes."
        );

        if (env.IsDevelopment())
        {
            return new
            {
                message = "Dev-only: use this code to call /auth/password/reset",
                code = code
            };
        }

        logger.LogInformation("Password reset code sent to {Email}", dto.Email);

        return new { message = "If the account exists, a reset code has been sent." };
    }

    public async Task<string> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email);
        // Always return success to avoid account enumeration
        if (user == null) return "Password has been reset if the account exists.";

        var redisKey = $"password_reset:{dto.Email}";
        var storedCode = await redis.GetAsync<string>(redisKey);

        if (storedCode != dto.Code)
        {
            throw new ArgumentException("Invalid or expired reset code.");
        }

        // Generate the actual identity token required for reset
        var token = await users.GeneratePasswordResetTokenAsync(user);
        
        var result = await users.ResetPasswordAsync(user, token, dto.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new ArgumentException(errors);
        }

        // Invalidate the code
        await redis.DeleteAsync(redisKey);

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

    public async Task<AuthResponse> VerifyEmailAsync(VerifyEmailDto dto)
    {
        var redisKey = $"email_verification:{dto.Email}";
        var storedCode = await redis.GetAsync<string>(redisKey);

        if (storedCode != dto.Code)
        {
            throw new ArgumentException("Invalid or expired verification code.");
        }

        var user = await users.FindByEmailAsync(dto.Email);
        if (user == null) throw new KeyNotFoundException("User not found.");

        if (user.EmailConfirmed) return await tokens.CreateAuthResponseAsync(user);

        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        
        await redis.DeleteAsync(redisKey);

        logger.LogInformation("Email verified for user {UserId}", user.Id);
        return await tokens.CreateAuthResponseAsync(user);
    }

    public async Task ResendVerificationEmailAsync(string email)
    {
        var user = await users.FindByEmailAsync(email);
        if (user == null) 
        {
            // Avoid enumeration: do nothing or pretend success
            return; 
        }

        if (user.EmailConfirmed)
        {
            throw new InvalidOperationException("Email is already verified.");
        }

        await SendVerificationCodeAsync(user);
    }

    private async Task CheckEmailRateLimitAsync(string email)
    {
        var limit = config.GetValue<int>("RateLimiting:EmailSendLimitPerHour", 5);
        var key = $"rate_limit:email:{email}";
        var count = await redis.IncrementAsync(key, TimeSpan.FromHours(1));

        if (count > limit)
        {
            logger.LogWarning("Rate limit exceeded for {Email}", email);
            throw new InvalidOperationException("Too many emails sent. Please try again in an hour.");
        }
    }

    private async Task SendVerificationCodeAsync(AppUser user)
    {
        await CheckEmailRateLimitAsync(user.Email!);
        var code = Random.Shared.Next(100000, 999999).ToString();
        var redisKey = $"email_verification:{user.Email}";
        
        // Store in Redis for 24 hours
        await redis.SetAsync(redisKey, code, TimeSpan.FromHours(24));
        
        // Send email
        await emailService.SendEmailAsync(
            user.Email!, 
            "Verify your Conquest account", 
            $"Your verification code is: <b>{code}</b>. It expires in 24 hours."
        );
        
        logger.LogInformation("Verification code sent to {Email}", user.Email);
    }

    public async Task DeleteAccountAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found.");

        // Hard delete the user. Cascade delete should handle related entities if configured.
        // If soft delete is preferred later, change this logic.
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to delete user: {errors}");
        }

        logger.LogInformation("User account deleted: {UserId}", userId);
    }
}
