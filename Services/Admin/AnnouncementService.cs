using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ping.Services.Admin
{
    public class AnnouncementService : IAnnouncementService
    {
        private readonly string _filePath = Path.Combine(Directory.GetCurrentDirectory(), "announcement.json");
        private string? _cachedMessage;
        private bool _isCached = false;
        private readonly object _lock = new object();

        public async Task<string?> GetAnnouncementAsync()
        {
            if (_isCached)
            {
                return _cachedMessage;
            }

            if (!File.Exists(_filePath))
            {
                lock (_lock)
                {
                    _cachedMessage = null;
                    _isCached = true;
                }
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_filePath);
                var data = JsonSerializer.Deserialize<AnnouncementData>(json);
                lock (_lock)
                {
                    _cachedMessage = data?.Message;
                    _isCached = true;
                }
                return _cachedMessage;
            }
            catch
            {
                return null;
            }
        }

        public async Task SetAnnouncementAsync(string? message)
        {
            var data = new AnnouncementData { Message = message };
            var json = JsonSerializer.Serialize(data);
            await File.WriteAllTextAsync(_filePath, json);

            lock (_lock)
            {
                _cachedMessage = message;
                _isCached = true;
            }
        }

        private class AnnouncementData
        {
            public string? Message { get; set; }
        }
    }
}
