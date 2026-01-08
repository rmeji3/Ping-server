using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Ping.Services.Images;
using Ping.Services.Moderation;
using System.Security.Claims;

namespace Ping.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ImagesController(IImageService imageService, IModerationService moderationService) : ControllerBase
{
    // POST /api/images?folder=events
    [HttpPost]
    public async Task<ActionResult<ImageUploadResponse>> UploadImage(IFormFile file, [FromQuery] string folder = "uploads")
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        // Validate folder to prevent arbitrary path writes? 
        // Simple allowlist: events, reviews
        var allowedFolders = new[] { "events", "reviews" };
        if (!allowedFolders.Contains(folder.ToLower()))
        {
            return BadRequest("Invalid folder. Allowed: events, reviews");
        }

        // Validate content type before reading
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        if (!allowedTypes.Contains(file.ContentType))
             return BadRequest("Invalid file type.");

        // Moderate Image
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());
            var dataUrl = $"data:{file.ContentType};base64,{base64}";
            
            var moderation = await moderationService.CheckImageAsync(dataUrl);
            if (moderation.IsFlagged)
            {
                return BadRequest($"Image rejected by moderation: {moderation.Reason}");
            }
        }

        try
        {
            var (original, thumbnail) = await imageService.ProcessAndUploadImageAsync(file, folder, userId);
            return Ok(new ImageUploadResponse(original, thumbnail));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public record ImageUploadResponse(string Url, string ThumbnailUrl);
