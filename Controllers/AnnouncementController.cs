using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Ping.Services.Admin;
using System.Threading.Tasks;

namespace Ping.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/announcement")]
    [Route("api/v{version:apiVersion}/announcement")]
    public class AnnouncementController(IAnnouncementService announcementService) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAnnouncement()
        {
            var message = await announcementService.GetAnnouncementAsync();
            return Ok(new { message });
        }
    }
}
