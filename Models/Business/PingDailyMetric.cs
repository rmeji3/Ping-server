using System.ComponentModel.DataAnnotations;
using Ping.Models.Pings;

namespace Ping.Models.Business;

public class PingDailyMetric
{
    public int Id { get; set; }

    public int PingId { get; set; }
    public Ping.Models.Pings.Ping? Ping { get; set; }

    public DateOnly Date { get; set; }

    public int ViewCount { get; set; }
}

