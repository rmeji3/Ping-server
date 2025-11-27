namespace Conquest.Dtos.Reviews;

public record UserReviewsDto(
    ReviewDto Review,
    List<ReviewDto> History
);
