using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ping.Data.App;
using Ping.Dtos.Common;
using Ping.Dtos.Reports;
using Ping.Models.Reports;
using Ping.Services.Storage;

namespace Ping.Services.Reports
{
    public class ReportService(AppDbContext context, IStorageService storageService) : IReportService
    {
        public async Task<Report> CreateReportAsync(string reporterId, CreateReportDto dto, IFormFile? screenshot = null)
        {
            // Validate: Bug reports don't need a TargetId, but all other report types do
            if (dto.TargetType != ReportTargetType.Bug && string.IsNullOrWhiteSpace(dto.TargetId))
            {
                throw new ArgumentException("TargetId is required for content reports (Ping, Review, Profile, Event, etc.).");
            }

            string? screenshotUrl = null;

            // Upload screenshot if provided
            if (screenshot != null)
            {
                var key = $"reports/{reporterId}/{Guid.NewGuid()}{Path.GetExtension(screenshot.FileName)}";
                screenshotUrl = await storageService.UploadFileAsync(screenshot, key);
            }

            var report = new Report
            {
                ReporterId = reporterId,
                TargetId = dto.TargetId,
                TargetType = dto.TargetType,
                Reason = dto.Reason,
                Description = dto.Description,
                ScreenshotUrl = screenshotUrl,
                CreatedAt = DateTime.UtcNow,
                Status = ReportStatus.Pending
            };

            context.Reports.Add(report);
            await context.SaveChangesAsync();

            return report;
        }

        public async Task<PaginatedResult<Report>> GetReportsAsync(PaginationParams pagination, ReportStatus? status = null)
        {
            var query = context.Reports.AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(r => r.Status == status.Value);
            }

            query = query.OrderByDescending(r => r.CreatedAt);

            return await PaginatedResult<Report>.CreateAsync(query, pagination.PageNumber, pagination.PageSize);
        }
    }
}

