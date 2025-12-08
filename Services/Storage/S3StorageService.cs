using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Storage;

public class S3StorageService(IAmazonS3 s3Client, IConfiguration configuration, ILogger<S3StorageService> logger) : IStorageService
{
    private readonly string _bucketName = configuration["AWS:BucketName"] 
                                          ?? throw new InvalidOperationException("AWS:BucketName is not configured.");

    public async Task<string> UploadFileAsync(IFormFile file, string key)
    {
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = file.OpenReadStream(),
                ContentType = file.ContentType
                // Removed CannedACL because the bucket enforces ownership (ACLs disabled)
            };
            
            // If you want private files + presigned URLs, remove CannedACL and generate presigned URL instead.
            // For profile pics, public read is usually fine if the bucket allows it.
            // If the bucket blocks public ACLs (recommended for security), we should use CloudFront or Presigned URLs.
            // For now, I'm assuming a standard public-read bucket or a bucket policy that allows public access to this folder.
            // If the user wants secure pre-signed URLs, we can switch.
            // Given the prompt "production... AWS service", simple public access is often the first step for profile pics.
            // However, modern S3 often blocks ACLs.
            // Let's assume standard PutObject and return the URL. 
            // If it fails due to ACLs, users often need to adjust bucket settings.
            
            // NOTE: Using CannedACL.PublicRead might fail if "Block Public Access" is ON for the bucket.
            // A safer default for "production" without complex setup is usually just uploading private and using CloudFront, 
            // OR uploading public if the user explicitly configured the bucket for it.
            // I will leave CannedACL here but if it fails we might need to remove it and rely on Bucket Policy.
            
            await s3Client.PutObjectAsync(request);

            // Construct the URL. 
            // Virtual-hosted-style access: https://bucket-name.s3.region-code.amazonaws.com/key-name
            var region = configuration["AWS:Region"];
            var url = $"https://{_bucketName}.s3.{region}.amazonaws.com/{key}";
            
            return url;
        }
        catch (AmazonS3Exception e)
        {
            logger.LogError(e, "Error encountered on server. Message:'{Message}' when writing an object", e.Message);
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unknown encountered on server. Message:'{Message}' when writing an object", e.Message);
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
