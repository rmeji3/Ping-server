using Conquest.Dtos.Tags;
using Conquest.Services.Tags;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Conquest.Controllers.Tags;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Require auth for everything, maybe allow anonymous for search/popular later?
public class TagsController(ITagService tagService) : ControllerBase
{
    [HttpGet("popular")]
    [AllowAnonymous] // Public can see popular tags
    public async Task<ActionResult<IEnumerable<TagDto>>> GetPopularTags([FromQuery] int count = 20)
    {
        var tags = await tagService.GetPopularTagsAsync(count);
        return Ok(tags);
    }

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<TagDto>>> SearchTags([FromQuery] string q, [FromQuery] int count = 20)
    {
        var tags = await tagService.SearchTagsAsync(q, count);
        return Ok(tags);
    }

    // Admin endpoints - for now just [Authorize], ideally would be [Authorize(Roles = "Admin")]
    [Authorize(Roles = "Admin")]
    [HttpPost("admin/{id}/approve")]
    public async Task<IActionResult> ApproveTag(int id)
    {
        await tagService.ApproveTagAsync(id);
        return Ok();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("admin/{id}/ban")]
    public async Task<IActionResult> BanTag(int id)
    {
        await tagService.BanTagAsync(id);
        return Ok();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("admin/{id}/merge/{targetId}")]
    public async Task<IActionResult> MergeTag(int id, int targetId)
    {
        await tagService.MergeTagAsync(id, targetId);
        return Ok();
    }
}
