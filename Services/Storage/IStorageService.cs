using Microsoft.AspNetCore.Http;

namespace Conquest.Services.Storage;

public interface IStorageService
{
    Task<string> UploadFileAsync(IFormFile file, string key);
    Task DeleteFileAsync(string key);
}
