namespace Conquest.Services.Friends;

public interface IFriendService
{
    /// <summary>
    /// Returns the user IDs of all friends of the given user.
    /// </summary>
    Task<IReadOnlyList<string>> GetFriendIdsAsync(string userId);
}