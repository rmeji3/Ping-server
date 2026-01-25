using Ping.Dtos.Activities;
using Ping.Services.Activities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Asp.Versioning;

namespace Ping.Controllers.Activities
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/ping-activities")]
    [Route("api/v{version:apiVersion}/ping-activities")]
    [Authorize]
    public class PingActivitiesController(IPingActivityService activityService) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<PingActivityDetailsDto>> Create([FromBody] CreatePingActivityDto dto)
        {
            return await CreateInternal(dto);
        }

        [HttpPost("/api/pings/{pingId}/activities")]
        [HttpPost("/api/v{version:apiVersion}/pings/{pingId}/activities")]
        public async Task<ActionResult<PingActivityDetailsDto>> CreateForPing(int pingId, [FromBody] CreatePingActivityDto dto)
        {
            if (dto.PingId != 0 && dto.PingId != pingId)
            {
                return BadRequest("PingId in body does not match PingId in route.");
            }
            return await CreateInternal(dto with { PingId = pingId });
        }

        private async Task<ActionResult<PingActivityDetailsDto>> CreateInternal(CreatePingActivityDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null) return Unauthorized();

                var result = await activityService.CreatePingActivityAsync(dto, userId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult<global::Ping.Dtos.Common.PaginatedResult<PingActivityDetailsDto>>> Search([FromQuery] ActivitySearchDto searchDto)
        {
            var result = await activityService.SearchActivitiesAsync(searchDto);
            return Ok(result);
        }

        [HttpGet("/api/pings/{pingId}/activities")]
        [HttpGet("/api/v{version:apiVersion}/pings/{pingId}/activities")]
        public async Task<ActionResult<global::Ping.Dtos.Common.PaginatedResult<PingActivityDetailsDto>>> GetForPing(int pingId, [FromQuery] ActivitySearchDto searchDto)
        {
            searchDto.PingId = pingId;
            var result = await activityService.SearchActivitiesAsync(searchDto);
            return Ok(result);
        }
    }
}

