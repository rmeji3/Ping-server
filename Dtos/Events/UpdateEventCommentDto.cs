using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Events
{
    public record UpdateEventCommentDto([Required, MaxLength(500)] string Content);
}
