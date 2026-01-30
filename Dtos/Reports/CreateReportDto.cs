using System;
using Ping.Models.Reports;
using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Reports
{
    public class CreateReportDto
    {
        [MaxLength(256)]
        public string? TargetId { get; set; }  // Null for Bug reports
        [Required]
        public ReportTargetType TargetType { get; set; }
        [Required(ErrorMessage = "Reason is required."), MaxLength(100)]
        public string Reason { get; set; } = string.Empty;
        [MaxLength(1000)]
        public string? Description { get; set; }
        public IFormFile? Screenshot { get; set; }
    }
}

