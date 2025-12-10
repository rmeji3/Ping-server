using Prometheus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Conquest.Middleware;

public class ResponseMetricMiddleware
{
    private readonly RequestDelegate _next;
    
    // Histogram for Response Size
    private static readonly Histogram ResponseSize = Metrics.CreateHistogram(
        "http_response_size_bytes",
        "Size of HTTP response bodies in bytes.",
        new HistogramConfiguration
        {
            // Buckets: 100B, 500B, 1KB, 5KB, 10KB, 50KB, 100KB, 500KB, 1MB, 5MB, 10MB
            Buckets = new[] { 100.0, 500, 1024, 5120, 10240, 51200, 102400, 512000, 1048576, 5242880, 10485760 },
            LabelNames = new[] { "method", "endpoint", "code" }
        });

    // Histogram for Request Size (Uploads)
    private static readonly Histogram RequestSize = Metrics.CreateHistogram(
        "http_request_size_bytes",
        "Size of HTTP request bodies in bytes.",
        new HistogramConfiguration
        {
            Buckets = new[] { 100.0, 500, 1024, 5120, 10240, 51200, 102400, 512000, 1048576, 5242880, 10485760 },
            LabelNames = new[] { "method", "endpoint" }
        });

    public ResponseMetricMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Capture Request Size
        if (context.Request.ContentLength.HasValue)
        {
            var endpoint = GetEndpointName(context);
            RequestSize.WithLabels(context.Request.Method, endpoint).Observe(context.Request.ContentLength.Value);
        }

        // We can't easily capture Response Size for chunked responses without wrapping the stream, 
        // which can be performance heavy. We will capture checking ContentLength after the response is prepared.
        // Note: This often works for smaller responses or when Content-Length is explicitly set.
        
        context.Response.OnCompleted(() =>
        {
            if (context.Response.ContentLength.HasValue)
            {
                var endpoint = GetEndpointName(context);
                var code = context.Response.StatusCode.ToString();
                ResponseSize.WithLabels(context.Request.Method, endpoint, code).Observe(context.Response.ContentLength.Value);
            }
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private string GetEndpointName(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.DisplayName ?? "unknown";
    }
}
