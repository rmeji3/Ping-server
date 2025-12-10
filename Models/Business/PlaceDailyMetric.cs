using System.ComponentModel.DataAnnotations;
using Conquest.Models.Places;

namespace Conquest.Models.Business;

public class PlaceDailyMetric
{
    public int Id { get; set; }

    public int PlaceId { get; set; }
    public Place? Place { get; set; }

    public DateOnly Date { get; set; }

    public int ViewCount { get; set; }
}
