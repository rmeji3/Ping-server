using System;

namespace Ping.Models.Reports
{
    public enum ReportTargetType
    {
        Ping,
        PingActivity,
        Review,
        Profile,
        Bug
    }

    public enum ReportStatus
    {
        Pending,
        Reviewed,
        Dismissed
    }

    public class Report
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ReporterId { get; set; }
        public string TargetId { get; set; } = string.Empty;
        public ReportTargetType TargetType { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ReportStatus Status { get; set; } = ReportStatus.Pending;
    }
}

