using Conquest.Dtos.Activities;

namespace Conquest.Dtos.Places
{
    public record UpsertPlaceDto(string Name, string Address, double Latitude, double Longitude, bool IsPublic);
    public record PlaceDetailsDto(
        int Id, 
        string Name, 
        string Address, 
        double Latitude, 
        double Longitude, 
        bool IsPublic,
        bool IsOwner,
        bool IsFavorited,
        ActivitySummaryDto[] Activities,
        string[] ActivityKinds
        );

}
