using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;

namespace NickScanCentralImagingPortal.API.Attributes
{
    /// <summary>
    /// Attribute to enable response caching on controller actions
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class CachedAttribute : Attribute, IAsyncActionFilter
    {
        private readonly int _durationSeconds;

        public CachedAttribute(int durationSeconds = 300)
        {
            _durationSeconds = durationSeconds;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var cacheService = context.HttpContext.RequestServices
                .GetService<NickScanCentralImagingPortal.Core.Interfaces.ICacheService>();

            if (cacheService == null)
            {
                // Caching not available, proceed without caching
                await next();
                return;
            }

            // Generate cache key from route and query parameters
            var cacheKey = GenerateCacheKey(context.HttpContext.Request);

            try
            {
                // Try to get from cache
                var cachedResponse = await cacheService.GetAsync<CachedResponse>(cacheKey);

                if (cachedResponse != null)
                {
                    // Return cached response
                    var result = new ContentResult
                    {
                        Content = cachedResponse.Content,
                        ContentType = cachedResponse.ContentType,
                        StatusCode = cachedResponse.StatusCode
                    };
                    context.Result = result;
                    context.HttpContext.Response.Headers["X-Cache"] = "HIT";
                    return;
                }

                // Cache miss - execute action
                context.HttpContext.Response.Headers["X-Cache"] = "MISS";
                var executedContext = await next();

                // Cache the response if successful
                if (executedContext.Result is ObjectResult objectResult && objectResult.StatusCode == 200)
                {
                    var response = new CachedResponse
                    {
                        Content = JsonSerializer.Serialize(objectResult.Value),
                        ContentType = "application/json",
                        StatusCode = 200
                    };

                    await cacheService.SetAsync(cacheKey, response, TimeSpan.FromSeconds(_durationSeconds));
                }
            }
            catch (Exception)
            {
                // On cache error, just proceed without caching
                await next();
            }
        }

        private string GenerateCacheKey(HttpRequest request)
        {
            var keyBuilder = new StringBuilder();
            keyBuilder.Append($"{request.Path}");

            foreach (var (key, value) in request.Query.OrderBy(x => x.Key))
            {
                keyBuilder.Append($"|{key}-{value}");
            }

            return keyBuilder.ToString();
        }

        private class CachedResponse
        {
            public string Content { get; set; } = string.Empty;
            public string ContentType { get; set; } = "application/json";
            public int StatusCode { get; set; } = 200;
        }
    }
}

