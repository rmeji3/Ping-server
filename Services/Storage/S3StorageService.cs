using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Storage;

public class S3StorageService(IAmazonS3 s3Client, IConfiguration configuration, ILogger<S3StorageService> logger) : IStorageService
{
    private readonly string _bucketName = configuration["AWS:BucketName"] 
                                          ?? throw new InvalidOperationException("AWS:BucketName is not configured.");

    public async Task<string> UploadFileAsync(IFormFile file, string key)
    {
        try
        {
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            logger.LogInformation("Uploading to S3: Bucket={Bucket}, Key={Key}, Size={Size}bytes, ContentType={ContentType}", 
                _bucketName, key, memoryStream.Length, file.ContentType);

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = memoryStream,
                ContentType = file.ContentType
            };
            
            await s3Client.PutObjectAsync(request);

            var region = configuration["AWS:Region"];
            var url = $"https://{_bucketName}.s3.{region}.amazonaws.com/{key}";
            
            logger.LogInformation("Successfully uploaded to S3: {Url}", url);
            return url;
        }
        catch (AmazonS3Exception e)
        {
            logger.LogError(e, "S3 error uploading. ErrorCode={ErrorCode}, StatusCode={StatusCode}, Message='{Message}'",
                e.ErrorCode, e.StatusCode, e.Message);
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unknown error uploading. Message:'{Message}'", e.Message);
            throw;
        }
    }

    public async Task DeleteFileAsync(string key)
    {
        try
        {
            var deleteObjectRequest = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };
            await s3Client.DeleteObjectAsync(deleteObjectRequest);
        }
        catch (AmazonS3Exception e)
        {
            logger.LogError(e, "Error encountered on server. Message:'{Message}' when deleting an object", e.Message);
            throw;
        }
    }
}

