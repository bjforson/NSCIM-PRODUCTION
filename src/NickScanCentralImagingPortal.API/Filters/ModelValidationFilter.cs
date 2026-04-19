using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NickScanCentralImagingPortal.API.Filters
{
    /// <summary>
    /// Global model validation filter
    /// Automatically validates all incoming request models and returns 400 Bad Request with validation errors
    /// </summary>
    public class ModelValidationFilter : IActionFilter
    {
        private readonly ILogger<ModelValidationFilter> _logger;

        public ModelValidationFilter(ILogger<ModelValidationFilter> logger)
        {
            _logger = logger;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.IsValid)
            {
                var errors = context.ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                    );

                var result = new
                {
                    Message = "Validation failed",
                    Errors = errors,
                    StatusCode = 400
                };

                _logger.LogWarning("Model validation failed: {Errors}",
                    string.Join(", ", errors.Select(e => $"{e.Key}: {string.Join(", ", e.Value)}")));

                context.Result = new BadRequestObjectResult(result);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            // No action needed after execution
        }
    }
}

