using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ping.Dtos.Stickers;
using Ping.Services.Stickers;

namespace Ping.Controllers.Stickers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class StickersController : ControllerBase
{
    private readonly IStickerService _stickerService;

    public StickersController(IStickerService stickerService)
    {
        _stickerService = stickerService;
    }

    // GET /api/stickers/catalog
    [HttpGet("catalog")]
    public async Task<ActionResult<List<StickerDto>>> GetCatalog()
    {
        return Ok(await _stickerService.GetCatalogAsync());
    }

    // GET /api/stickers/owned
    [HttpGet("owned")]
    public async Task<ActionResult<List<StickerDto>>> GetOwned()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        return Ok(await _stickerService.GetOwnedStickersAsync(userId));
    }

    // GET /api/stickers/marketplace — stickers in the current claim rotation
    [HttpGet("marketplace")]
    public async Task<ActionResult<List<MarketplaceStickerDto>>> GetMarketplace()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        return Ok(await _stickerService.GetMarketplaceAsync(userId));
    }

    // POST /api/stickers/{id}/claim — claim a rotation sticker for the current user
    [HttpPost("{id}/claim")]
    public async Task<IActionResult> ClaimSticker(string id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        try
        {
            await _stickerService.ClaimStickerAsync(userId, id);
            return Ok(new { message = "Sticker claimed successfully." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // GET /api/stickers/placements/{userId}
    [HttpGet("placements/{userId}")]
    public async Task<ActionResult<List<ProfileStickerPlacementDto>>> GetPlacements(string userId)
    {
        return Ok(await _stickerService.GetPlacementsAsync(userId));
    }

    // PUT /api/stickers/placements  — save the current user's profile header placements
    [HttpPut("placements")]
    public async Task<IActionResult> SavePlacements(SaveProfileStickerPlacementsDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        try
        {
            await _stickerService.SavePlacementsAsync(userId, dto.Placements ?? new List<SaveStickerPlacementDto>());
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // GET /api/stickers/{id}/raw — public proxy endpoint to bypass S3 CORS policies for player controls
    [HttpGet("{id}/raw")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRawStickerFile(string id)
    {
        var sticker = await _stickerService.GetStickerByIdAsync(id);
        if (sticker == null || string.IsNullOrEmpty(sticker.ImageUrl))
        {
            return NotFound();
        }

        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(sticker.ImageUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, "Failed to retrieve file from storage.");
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}
