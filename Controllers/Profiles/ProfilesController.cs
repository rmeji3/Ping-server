using System.Security.Claims;
using Conquest.Dtos.Profiles;
using Conquest.Services.Profiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Conquest.Controllers.Profiles;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfilesController(IProfileService profileService) : ControllerBase
{
    // GET /api/profiles/me
    [HttpGet("me")]
    public async Task<ActionResult<PersonalProfileDto>> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        try
        {
            var profile = await profileService.GetMyProfileAsync(userId);
            return Ok(profile);
        }
        catch (KeyNotFoundException)
        {
            return Unauthorized();
        }
    }
    
    // GET /api/profiles/search?username=someUsername
    [HttpGet("search")]
    public async Task<ActionResult<List<ProfileDto>>> Search([FromQuery] string username)
    {
        var yourUsername = User.FindFirstValue(ClaimTypes.Name);
        if (yourUsername is null) return Unauthorized();

        try
        {
            var users = await profileService.SearchProfilesAsync(username, yourUsername);
            return Ok(users);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
    [HttpPost("me/image")]
    public async Task<ActionResult<string>> UploadProfileImage(IFormFile file)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        try
        {
            var url = await profileService.UpdateProfileImageAsync(userId, file);
            return Ok(new { Url = url });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            // Log ex
            return StatusCode(500, "An error occurred while uploading the image.");
        }
    }
}