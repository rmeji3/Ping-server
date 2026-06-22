using System.Threading.Tasks;

namespace Ping.Services.Admin
{
    public interface IAnnouncementService
    {
        Task<string?> GetAnnouncementAsync();
        Task SetAnnouncementAsync(string? message);
    }
}
