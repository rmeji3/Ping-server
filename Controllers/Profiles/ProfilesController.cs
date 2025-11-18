using System.Security.Claims;
using Conquest.Data.Auth;
using Conquest.Dtos.Auth;
using Conquest.Dtos.Profiles;
using Conquest.Models.AppUsers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Conquest.Controllers.Profiles;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfilesController : Controller
{
    private readonly AuthDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    public ProfilesController(AuthDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }
    
    // GET /api/profiles/me
    [HttpGet("me")]
    public async Task<ActionResult<ProfileDto>> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        return new ProfileDto(user.Id, user.UserName, user.FirstName, user.LastName, user.ProfileImageUrl);
    }
    
    // GET /api/profiles/search?username=someusername
    [HttpGet("search")]
    public async Task<ActionResult<List<ProfileDto>>> Search([FromQuery] string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("Username query parameter is required.");

        // Case-insensitive search
        var normalized = username.ToLower();

        var users = await _userManager.Users
            .Where(u => u.UserName.ToLower().StartsWith(normalized))
            .OrderBy(u => u.UserName)        // stable order
            .Take(15)                        // limit results
            .Select(u => new ProfileDto(
                u.Id,
                u.UserName,
                u.FirstName,
                u.LastName,
                u.ProfileImageUrl
            ))
            .ToListAsync();

        return Ok(users);
    }

    
}