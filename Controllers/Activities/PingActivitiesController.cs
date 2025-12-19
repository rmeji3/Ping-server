using Ping.Dtos.Activities;
using Ping.Services.Activities;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Ping.Controllers.Activities
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/ping-activities")]
    [Route("api/v{version:apiVersion}/ping-activities")]
    public class PingActivitiesController(IPingActivityService activityService) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<PingActivityDetailsDto>> Create([FromBody] CreatePingActivityDto dto)
        {
            try
            {
                var result = await activityService.CreatePingActivityAsync(dto);
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
                return Conflict(new { error = ex.Message });
            }
        }
    }
}

