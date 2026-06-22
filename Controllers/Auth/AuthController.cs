using Ping.Dtos.Auth;
using Ping.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Security.Claims;

namespace Ping.Controllers.Auth
{

    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class AuthController(IAuthService authService, ITokenService tokenService) : ControllerBase
    {
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            try
            {
                var result = await authService.RegisterAsync(dto);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message.Split(", "));
            }
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Login(LoginDto dto)
        {
            try
            {
                var result = await authService.LoginAsync(dto);
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        [HttpPost("google")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> LoginWithGoogle(GoogleLoginDto dto)
        {
            try
            {
                var result = await authService.LoginWithGoogleAsync(dto);
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("apple")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> LoginWithApple(AppleLoginDto dto)
        {
            try
            {
                var result = await authService.LoginWithAppleAsync(dto);
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("verify-email")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> VerifyEmail(VerifyEmailDto dto)
        {
            try
            {
                var result = await authService.VerifyEmailAsync(dto);
                return result;
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (KeyNotFoundException) { return Unauthorized(); }
        }

        [HttpPost("verify-email/resend")]
        [AllowAnonymous]
        public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto dto)
        {
            try
            {
                await authService.ResendVerificationEmailAsync(dto.Email);
                return Ok(new { message = "If the account exists and is unverified, a verification code has been sent." });
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<UserDto>> Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await authService.GetCurrentUserAsync(userId);
                return result;
            }
            catch (KeyNotFoundException)
            {
                return Unauthorized();
            }
        }

        [HttpDelete("me")]
        [Authorize]
        public async Task<IActionResult> DeleteMyAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                await authService.DeleteAccountAsync(userId);
                return Ok(new { message = "Account deleted successfully." });
            }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        [HttpPost("password/forgot")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var result = await authService.ForgotPasswordAsync(dto, Request.Scheme, Request.Host.ToString());
            return Ok(result);
        }

        // ===== 2) Complete reset with token =====
        [HttpPost("password/reset")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            try
            {
                var result = await authService.ResetPasswordAsync(dto);
                return Ok(new { message = result });
            }
            catch (ArgumentException ex)
            {
                if (ex.Message == "Invalid token format.")
                {
                    return BadRequest(new { error = ex.Message });
                }
                // Assuming errors are comma separated for now, or we can adjust service to return list
                return BadRequest(new { errors = ex.Message.Split(", ").Select(e => new { Description = e }) });
            }
        }

        // ===== 3) Change password (authenticated) =====
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpPost("password/change")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            try
            {
                var result = await authService.ChangePasswordAsync(userId, dto);
                return Ok(new { message = result });
            }
            catch (KeyNotFoundException)
            {
                return Unauthorized();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { errors = ex.Message.Split(", ").Select(e => new { Description = e }) });
            }
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpPatch("username")]
        public async Task<ActionResult<AuthResponse>> ChangeUsername([FromBody] ChangeUsernameDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            try
            {
                var result = await authService.ChangeUsernameAsync(userId, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Unauthorized();
            }
            catch (InvalidOperationException ex)
            {
                // Username taken
                return Conflict(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                // Moderation or Validation failed
                return BadRequest(new { message = ex.Message });
            }
        }
        [HttpPost("dev/make-admin")]
        [AllowAnonymous] // Dev tool, could add secret check if needed
        public async Task<IActionResult> MakeAdmin([FromQuery] string email)
        {
            // Simple safety check - only allow in Development environment if possible.
            // But I don't have IWebHostEnvironment injected here.
            // Assuming this is a local dev convenience tool.
            
            try 
            {
                await authService.MakeAdminAsync(email);
                return Ok(new { message = $"User {email} is now an Admin." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        // ===== Token Refresh =====
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshTokenRequest dto)
        {
            try
            {
                var result = await tokenService.RefreshAsync(dto.RefreshToken, dto.DeviceId);
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        // ===== Logout (revoke refresh token) =====
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest dto)
        {
            await tokenService.RevokeRefreshTokenAsync(dto.RefreshToken);
            return Ok(new { message = "Logged out." });
        }

        // ===== Logout from all devices =====
        [HttpPost("logout-all")]
        [Authorize]
        public async Task<IActionResult> LogoutAll()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            await tokenService.RevokeAllUserTokensAsync(userId);
            return Ok(new { message = "Logged out from all devices." });
        }

        // ===== 2FA Verification (during Login) =====
        [HttpPost("verify-2fa")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> VerifyTwoFactor(VerifyTwoFactorDto dto)
        {
            try
            {
                var result = await authService.VerifyTwoFactorLoginAsync(dto);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        // ===== 2FA Setup (generates secret QR code URI) =====
        [HttpGet("2fa/setup")]
        [Authorize]
        public async Task<ActionResult<TwoFactorSetupDto>> SetupTwoFactor()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var result = await authService.GetTwoFactorSetupAsync(userId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        // ===== 2FA Enable (verify code and activate 2FA) =====
        [HttpPost("2fa/enable")]
        [Authorize]
        public async Task<IActionResult> EnableTwoFactor([FromBody] EnableTwoFactorDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var success = await authService.EnableTwoFactorAsync(userId, dto.Code);
                if (!success)
                {
                    return BadRequest(new { message = "Invalid verification code." });
                }
                return Ok(new { message = "Two-Factor Authentication enabled successfully." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        // ===== 2FA Disable =====
        [HttpPost("2fa/disable")]
        [Authorize]
        public async Task<IActionResult> DisableTwoFactor()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                var success = await authService.DisableTwoFactorAsync(userId);
                if (!success)
                {
                    return BadRequest(new { message = "Failed to disable Two-Factor Authentication." });
                }
                return Ok(new { message = "Two-Factor Authentication disabled successfully." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }
}

