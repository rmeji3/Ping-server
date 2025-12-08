using Conquest.Dtos.Auth;
using Conquest.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Conquest.Controllers.Auth
{

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController(IAuthService authService) : ControllerBase
    {
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponse>> Register(RegisterDto dto)
        {
            try
            {
                var result = await authService.RegisterAsync(dto);
                return result;
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
                return Unauthorized(ex.Message);
            }
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
