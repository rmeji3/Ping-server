using Ping.Dtos.Auth;
using Ping.Models.AppUsers;
using Ping.Models.Analytics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using Ping.Services.Moderation;

namespace Ping.Services.Auth;

public class AuthService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    ITokenService tokens,
    Ping.Data.Auth.AuthDbContext db,
    IHostEnvironment env,
    ILogger<AuthService> logger,
    IModerationService moderationService,
    Ping.Services.Email.IEmailService emailService,
    Microsoft.Extensions.Configuration.IConfiguration config,
    Ping.Services.Google.GoogleAuthService googleAuthService,
    Ping.Services.Apple.AppleAuthService appleAuthService,
    Ping.Services.Redis.IRedisService redis) : IAuthService
{
    public async Task<object> RegisterAsync(RegisterDto dto)
    {
        var normalizedEmail = dto.Email.ToLowerInvariant();
        var user = new AppUser { 
            Email = normalizedEmail,
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
        var user = await users.FindByEmailAsync(dto.UserNameOrEmail) 
                   ?? await users.FindByNameAsync(dto.UserNameOrEmail);

        if (user is null)
        {
            logger.LogWarning("Login failed: User '{Identifier}' not found.", dto.UserNameOrEmail);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        if (user.IsBanned)
        {
             logger.LogWarning("Login failed: User '{Identifier}' is banned.", dto.UserNameOrEmail);
             var msg = string.IsNullOrWhiteSpace(user.BanReason) ? "Your account has been banned." : $"Your account has been banned: {user.BanReason}";
             throw new UnauthorizedAccessException(msg);
        }
        // comment out for testing
        if (!await users.IsEmailConfirmedAsync(user))
        {
            logger.LogWarning("Login failed: User '{Identifier}' not confirmed.", dto.UserNameOrEmail);
            throw new UnauthorizedAccessException("Please verify your email address.");
        }

        var result = await signIn.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning("Login failed: Invalid password for '{Identifier}'.", dto.UserNameOrEmail);
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

    public async Task<AuthResponse> LoginWithGoogleAsync(GoogleLoginDto dto)
    {
        // 1. Verify Token with Google
        // We need to inject GoogleAuthService. Since it's new, I'll resolve it or add it to constructor.
        // For now, let's assume it's injected. I will update the constructor below.
        
        var payload = await googleAuthService.VerifyGoogleTokenAsync(dto.IdToken);

        // 2. Check if user exists
        var user = await users.FindByEmailAsync(payload.Email);

        if (user == null)
        {
            // 3. Register new user
            user = new AppUser
            {
                UserName = await GenerateUniqueUsernameAsync(payload.Name, payload.Email),
                Email = payload.Email,
                FirstName = payload.GivenName ?? payload.Name,
                LastName = payload.FamilyName,
                EmailConfirmed = true, // Trusted from Google
                CreatedUtc = DateTimeOffset.UtcNow,
                ProfileImageUrl = payload.Picture
            };

            var result = await users.CreateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new Exception($"Failed to create Google user: {errors}");
            }
            
            logger.LogInformation("New user created via Google: {UserId}", user.Id);
        }
        else
        {
            // 4. Existing user - Ensure Email is confirmed if it wasn't
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await users.UpdateAsync(user);
            }
            
            // Check ban status
            if (user.IsBanned)
            {
                 var msg = string.IsNullOrWhiteSpace(user.BanReason) ? "Your account has been banned." : $"Your account has been banned: {user.BanReason}";
                 throw new UnauthorizedAccessException(msg);
            }
        }

        // 5. Log Activity (Reusing login logic would be better but for speed copying inline)
        user.LastLoginUtc = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);
        
        return await tokens.CreateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginWithAppleAsync(AppleLoginDto dto)
    {
        // 1. Verify Token with Apple
        var payload = await appleAuthService.VerifyAppleTokenAsync(dto.IdToken);

        // 2. Check if user exists by Email
        var user = await users.FindByEmailAsync(payload.Email);

        if (user == null)
        {
            // 3. Register new user
            // NOTE: Apple only sends FirstName/LastName on the FIRST login. 
            // If we missed it (e.g. user re-installing app), these might be null.
            string firstName = !string.IsNullOrWhiteSpace(dto.FirstName) ? dto.FirstName : "Apple";
            string lastName = !string.IsNullOrWhiteSpace(dto.LastName) ? dto.LastName : "User";

            user = new AppUser
            {
                UserName = await GenerateUniqueUsernameAsync(firstName, payload.Email),
                Email = payload.Email,
                FirstName = firstName,
                LastName = lastName,
                EmailConfirmed = true,
                CreatedUtc = DateTimeOffset.UtcNow
            };

            var result = await users.CreateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new Exception($"Failed to create Apple user: {errors}");
            }
            
            logger.LogInformation("New user created via Apple: {UserId}", user.Id);
        }
        else
        {
            // 4. Existing user
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await users.UpdateAsync(user);
            }

            if (user.IsBanned)
            {
                 var msg = string.IsNullOrWhiteSpace(user.BanReason) ? "Your account has been banned." : $"Your account has been banned: {user.BanReason}";
                 throw new UnauthorizedAccessException(msg);
            }
        }

        user.LastLoginUtc = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);
        
        return await tokens.CreateAuthResponseAsync(user);
    }

    private async Task<string> GenerateUniqueUsernameAsync(string name, string email)
    {
        // Strategy: Try name-based, then email-part, then append random numbers
        string baseName = "user";
        if (!string.IsNullOrWhiteSpace(name)) baseName = name.Replace(" ", "").ToLower();
        else if (!string.IsNullOrWhiteSpace(email)) baseName = email.Split('@')[0].ToLower();

        // Remove special chars
        baseName = new string(baseName.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(baseName)) baseName = "user";

        string candidate = baseName;
        int check = 0;
        while (await users.FindByNameAsync(candidate) != null)
        {
            check++;
            candidate = $"{baseName}{check}";
        }
        return candidate;
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
        var normalizedEmail = dto.Email.ToLowerInvariant();
        var user = await users.FindByEmailAsync(normalizedEmail);
        // Always return success to avoid account enumeration
        if (user == null)
        {
            logger.LogInformation("Forgot password requested for non-existent email: {Email}", dto.Email);
            return new { message = "If the account exists, a reset code has been sent." };
        }

        await CheckEmailRateLimitAsync(dto.Email);

        // Generate 6-digit code
        var code = Random.Shared.Next(100000, 999999).ToString();
        var redisKey = $"password_reset:{normalizedEmail}";
        
        // Store in Redis for 15 minutes
        await redis.SetAsync(redisKey, code, TimeSpan.FromMinutes(15));
        
        // Send email
        await emailService.SendEmailAsync(
            normalizedEmail, 
            "Reset your Ping password", 
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
        var normalizedEmail = dto.Email.ToLowerInvariant();
        var user = await users.FindByEmailAsync(normalizedEmail);
        // Always return success to avoid account enumeration
        if (user == null) return "Password has been reset if the account exists.";

        var redisKey = $"password_reset:{normalizedEmail}";
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

    public async Task RemoveAdminAsync(string email)
    {
        var user = await users.FindByEmailAsync(email);
        if (user == null) throw new KeyNotFoundException($"User with email {email} not found.");

        if (await users.IsInRoleAsync(user, "Admin"))
        {
            await users.RemoveFromRoleAsync(user, "Admin");
            logger.LogInformation("User {Email} demoted from Admin.", email);
        }
        else
        {
            logger.LogInformation("User {Email} is not an Admin.", email);
        }
    }

    public async Task<AuthResponse> VerifyEmailAsync(VerifyEmailDto dto)
    {
        var normalizedEmail = dto.Email.ToLowerInvariant();
        var redisKey = $"email_verification:{normalizedEmail}";
        var storedCode = await redis.GetAsync<string>(redisKey);

        if (storedCode != dto.Code)
        {
            throw new ArgumentException("Invalid or expired verification code.");
        }

        var user = await users.FindByEmailAsync(normalizedEmail);
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
        var normalizedEmail = email.ToLowerInvariant();
        var user = await users.FindByEmailAsync(normalizedEmail);
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
        var normalizedEmail = user.Email!.ToLowerInvariant();
        var redisKey = $"email_verification:{normalizedEmail}";
        
        // Generate 6-digit code
        var code = Random.Shared.Next(100000, 999999).ToString();
        
        // Store in Redis for 15 Minutes
        await redis.SetAsync(redisKey, code, TimeSpan.FromMinutes(15));
        
        // Send email
        await emailService.SendEmailAsync(
            user.Email!, 
            "Verify your Ping account", 
            $"Your verification code is: <b>{code}</b>. It expires in 15 Minutes."
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

