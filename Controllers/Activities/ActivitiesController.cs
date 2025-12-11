using Conquest.Dtos.Activities;
using Conquest.Services.Activities;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Conquest.Controllers.Activities
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/activities")]
    [Route("api/v{version:apiVersion}/activities")]
    public class ActivitiesController(IActivityService activityService) : ControllerBase
    {
        [HttpPost]
        public async Task<ActionResult<ActivityDetailsDto>> Create([FromBody] CreateActivityDto dto)
        {
            try
            {
                var result = await activityService.CreateActivityAsync(dto);
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
