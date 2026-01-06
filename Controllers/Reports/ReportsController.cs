using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Ping.Models.Reports;
using Ping.Dtos.Common;
using Ping.DTOs.Reports;
using Ping.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Ping.Controllers.Reports
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Authorize]
    public class ReportsController(IReportService reportService) : ControllerBase
    {
        /// <summary>
        /// Creates a new report. Supports optional screenshot attachment.
        /// Use multipart/form-data when uploading a screenshot.
        /// </summary>
        [HttpPost]
        [Consumes("multipart/form-data", "application/json")]
        public async Task<ActionResult> CreateReport([FromForm] CreateReportDto dto, IFormFile? screenshot = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null || !Guid.TryParse(userIdStr, out var userId))
            {
                return Unauthorized();
            }

            var report = await reportService.CreateReportAsync(userId, dto, screenshot);
            
            // returning 201 Created
            return StatusCode(201, report);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<PaginatedResult<Report>>> GetReports([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, [FromQuery] ReportStatus? status = null)
        {
            var pagination = new PaginationParams { PageNumber = pageNumber, PageSize = pageSize };
            var reports = await reportService.GetReportsAsync(pagination, status);
            return Ok(reports);
        }
    }
}

