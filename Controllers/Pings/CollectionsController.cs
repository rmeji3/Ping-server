using System.Security.Claims;
using Ping.Dtos.Common;
using Ping.Dtos.Pings;
using Ping.Services.Pings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Ping.Controllers.Pings
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Authorize]
    public class CollectionsController(ICollectionService collectionService, Ping.Services.Images.IImageService imageService) : ControllerBase
    {
        public class CreateCollectionRequest
        {
            public string Name { get; set; } = null!;
            public bool IsPublic { get; set; }
            public IFormFile? Image { get; set; }
        }

        public class UpdateCollectionRequest
        {
            public string? Name { get; set; }
            public bool? IsPublic { get; set; }
            public IFormFile? Image { get; set; }
        }
        // POST /api/collections
        // POST /api/collections
        [HttpPost]
        public async Task<ActionResult<CollectionDto>> Create([FromForm] CreateCollectionRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            string? imgUrl = null;
            string? thumbUrl = null;

            if (request.Image != null)
            {
                try
                {
                    var (original, thumb) = await imageService.ProcessAndUploadImageAsync(request.Image, "collections", userId);
                    imgUrl = original;
                    thumbUrl = thumb;
                }
                catch (Exception ex)
                {
                    return BadRequest("Image processing failed: " + ex.Message);
                }
            }

            var dto = new CreateCollectionDto(request.Name, request.IsPublic, imgUrl, thumbUrl);
            var result = await collectionService.CreateCollectionAsync(userId, dto);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // GET /api/collections/me
        [HttpGet("me")]
        public async Task<ActionResult<List<CollectionDto>>> GetMyCollections()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var result = await collectionService.GetMyCollectionsAsync(userId);
            return Ok(result);
        }

        // GET /api/collections/user/{userId}
        [HttpGet("user/{userId}")]
        public async Task<ActionResult<List<CollectionDto>>> GetUserPublicCollections(string userId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await collectionService.GetUserPublicCollectionsAsync(userId, currentUserId);
            return Ok(result);
        }

        // GET /api/collections/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CollectionDetailsDto>> GetById(int id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            try
            {
                var result = await collectionService.GetCollectionDetailsAsync(id, currentUserId);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // PATCH /api/collections/{id}
        // PATCH /api/collections/{id}
        [HttpPatch("{id:int}")]
        public async Task<ActionResult<CollectionDto>> Update(int id, [FromForm] UpdateCollectionRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();
            
            string? imgUrl = null;
            string? thumbUrl = null;

            if (request.Image != null)
            {
                 try
                {
                    var (original, thumb) = await imageService.ProcessAndUploadImageAsync(request.Image, "collections", userId);
                    imgUrl = original;
                    thumbUrl = thumb;
                }
                catch (Exception ex)
                {
                    return BadRequest("Image processing failed: " + ex.Message);
                }
            }

            var dto = new UpdateCollectionDto(request.Name, request.IsPublic, imgUrl, thumbUrl);

            try
            {
                var result = await collectionService.UpdateCollectionAsync(id, userId, dto);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // DELETE /api/collections/{id}
        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                await collectionService.DeleteCollectionAsync(id, userId);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // POST /api/collections/{id}/pings
        [HttpPost("{id:int}/pings")]
        public async Task<ActionResult> AddPing(int id, [FromBody] AddPingToCollectionDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                await collectionService.AddPingToCollectionAsync(id, userId, dto.PingId);
                return Ok();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // DELETE /api/collections/{id}/pings/{pingId}
        [HttpDelete("{id:int}/pings/{pingId:int}")]
        public async Task<ActionResult> RemovePing(int id, int pingId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            try
            {
                await collectionService.RemovePingFromCollectionAsync(id, userId, pingId);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
    }
}
