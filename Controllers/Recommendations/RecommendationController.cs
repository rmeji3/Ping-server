using Conquest.Dtos.Recommendations;
using Conquest.Services.Recommendations;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Conquest.Controllers.Recommendations
{

    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class RecommendationController(RecommendationService recommendationService) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<List<RecommendationDto>>> GetRecommendations(
            [FromQuery] string vibe, 
            [FromQuery] double latitude, 
            [FromQuery] double longitude,
            [FromQuery] double radius = 10.0) // Default 10km
        {
            if (string.IsNullOrWhiteSpace(vibe))
            {
                return BadRequest("Vibe is required.");
            }

            var results = await recommendationService.GetRecommendationsAsync(vibe, latitude, longitude, radius);
            return Ok(results);
        }
    }
}
