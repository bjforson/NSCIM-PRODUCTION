using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.Logging
{
    /// <summary>
    /// Extension methods for registering color-coded logging services
    /// </summary>
    public static class ColorCodedLoggingExtensions
    {
        /// <summary>
        /// Register color-coded logger for a specific service
        /// </summary>
        public static IServiceCollection AddColorCodedLogger<T>(this IServiceCollection services, string serviceCategory, string serviceId = "")
        {
            services.AddScoped<ColorCodedLogger>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<T>>();
                return new ColorCodedLogger(logger, serviceCategory, serviceId);
            });

            return services;
        }

        /// <summary>
        /// Register color-coded logger for ICUMS services
        /// </summary>
        public static IServiceCollection AddICUMSColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddColorCodedLogger<T>(ServiceCategories.ICUMS, serviceId);
        }

        /// <summary>
        /// Register color-coded logger for scanner services
        /// </summary>
        public static IServiceCollection AddScannerColorCodedLogger<T>(this IServiceCollection services, string scannerType, string serviceId = "")
        {
            return services.AddColorCodedLogger<T>(scannerType, serviceId);
        }

        /// <summary>
        /// Register color-coded logger for container services
        /// </summary>
        public static IServiceCollection AddContainerColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddColorCodedLogger<T>(ServiceCategories.CONTAINER_COMPLETENESS, serviceId);
        }

        /// <summary>
        /// Register color-coded logger for health check services
        /// </summary>
        public static IServiceCollection AddHealthCheckColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddColorCodedLogger<T>(ServiceCategories.HEALTH_CHECK, serviceId);
        }

        /// <summary>
        /// Register color-coded logger for performance monitoring services
        /// </summary>
        public static IServiceCollection AddPerformanceMonitoringColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddColorCodedLogger<T>(ServiceCategories.PERFORMANCE_MONITORING, serviceId);
        }

        /// <summary>
        /// Register color-coded logger for background services
        /// </summary>
        public static IServiceCollection AddBackgroundServiceColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddColorCodedLogger<T>(ServiceCategories.BACKGROUND_SERVICE, serviceId);
        }

        /// <summary>
        /// Register color-coded logger for API controllers
        /// </summary>
        public static IServiceCollection AddAPIControllerColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddColorCodedLogger<T>(ServiceCategories.API_CONTROLLER, serviceId);
        }

        /// <summary>
        /// Register color-coded logger for repositories
        /// </summary>
        public static IServiceCollection AddRepositoryColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddColorCodedLogger<T>(ServiceCategories.REPOSITORY, serviceId);
        }

        // Enhanced Logger Extensions

        /// <summary>
        /// Register enhanced color-coded logger for a specific service
        /// </summary>
        public static IServiceCollection AddEnhancedColorCodedLogger<T>(this IServiceCollection services, string serviceCategory, string serviceId = "")
        {
            services.AddScoped<EnhancedColorCodedLogger>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<T>>();
                return new EnhancedColorCodedLogger(logger, serviceCategory, serviceId);
            });

            return services;
        }

        /// <summary>
        /// Register enhanced color-coded logger for ICUMS services
        /// </summary>
        public static IServiceCollection AddICUMSEnhancedColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddEnhancedColorCodedLogger<T>(ServiceCategories.ICUMS, serviceId);
        }

        /// <summary>
        /// Register enhanced color-coded logger for scanner services
        /// </summary>
        public static IServiceCollection AddScannerEnhancedColorCodedLogger<T>(this IServiceCollection services, string scannerType, string serviceId = "")
        {
            return services.AddEnhancedColorCodedLogger<T>(scannerType, serviceId);
        }

        /// <summary>
        /// Register enhanced color-coded logger for container services
        /// </summary>
        public static IServiceCollection AddContainerEnhancedColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddEnhancedColorCodedLogger<T>(ServiceCategories.CONTAINER_COMPLETENESS, serviceId);
        }

        /// <summary>
        /// Register enhanced color-coded logger for health check services
        /// </summary>
        public static IServiceCollection AddHealthCheckEnhancedColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddEnhancedColorCodedLogger<T>(ServiceCategories.HEALTH_CHECK, serviceId);
        }

        /// <summary>
        /// Register enhanced color-coded logger for performance monitoring services
        /// </summary>
        public static IServiceCollection AddPerformanceMonitoringEnhancedColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddEnhancedColorCodedLogger<T>(ServiceCategories.PERFORMANCE_MONITORING, serviceId);
        }

        /// <summary>
        /// Register enhanced color-coded logger for background services
        /// </summary>
        public static IServiceCollection AddBackgroundServiceEnhancedColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddEnhancedColorCodedLogger<T>(ServiceCategories.BACKGROUND_SERVICE, serviceId);
        }

        /// <summary>
        /// Register enhanced color-coded logger for API controllers
        /// </summary>
        public static IServiceCollection AddAPIControllerEnhancedColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddEnhancedColorCodedLogger<T>(ServiceCategories.API_CONTROLLER, serviceId);
        }

        /// <summary>
        /// Register enhanced color-coded logger for repositories
        /// </summary>
        public static IServiceCollection AddRepositoryEnhancedColorCodedLogger<T>(this IServiceCollection services, string serviceId = "")
        {
            return services.AddEnhancedColorCodedLogger<T>(ServiceCategories.REPOSITORY, serviceId);
        }
    }
}
