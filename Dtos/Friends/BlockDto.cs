using System;

namespace Ping.Dtos.Friends
{
    public record BlockDto(
        string BlockedUserId, 
        string BlockedUserName, 
        string? BlockedUserProfileImageUrl,
        DateTime BlockedAt
    );
}
