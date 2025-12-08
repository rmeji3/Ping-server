namespace Conquest.Dtos.Tags;

public record TagDto(
    int Id,
    string Name,
    int? Count = null,
    bool IsApproved = false,
    bool IsBanned = false
);
