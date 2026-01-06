using System;
using Ping.Models.Reports;

namespace Ping.DTOs.Reports
{
    public class CreateReportDto
    {
        public string? TargetId { get; set; }  // Null for Bug reports
        public ReportTargetType TargetType { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}

