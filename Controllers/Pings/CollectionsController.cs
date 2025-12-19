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
    public class CollectionsController(ICollectionService collectionService) : ControllerBase
    {
        // POST /api/collections
        [HttpPost]
        public async Task<ActionResult<CollectionDto>> Create([FromBody] CreateCollectionDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

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
        [HttpPatch("{id:int}")]
        public async Task<ActionResult<CollectionDto>> Update(int id, [FromBody] UpdateCollectionDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

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
