using System.Security.Claims;
using Ping.Dtos.Reviews;
using Ping.Services.Reviews;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;

namespace Ping.Controllers.Reviews
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/reviews")]
    [Route("api/v{version:apiVersion}/reviews")]
    [Authorize]
    public class ReviewLookupController(IReviewService reviewService) : ControllerBase
    {
        // GET /api/v1/reviews/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ExploreReviewDto>> GetById(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            try
            {
                var result = await reviewService.GetReviewByIdAsync(id, userId);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("Review not found.");
            }
        }
    }
}
