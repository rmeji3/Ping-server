using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Ping.Dtos.Common;
using Ping.Dtos.Reports;
using Ping.Models.Reports;

namespace Ping.Services.Reports
{
    public interface IReportService
    {
        // Optional screenshotUrl parameter for when screenshot is already uploaded
        Task<Report> CreateReportAsync(string reporterId, CreateReportDto dto, IFormFile? screenshot = null);
        Task<PaginatedResult<Report>> GetReportsAsync(PaginationParams pagination, ReportStatus? status = null);
    }
}

