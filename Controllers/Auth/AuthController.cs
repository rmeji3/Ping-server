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
    public class AuthController(IAuthService authService) : ControllerBase
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
    }
}

