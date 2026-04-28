using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory; // Added for MemoryCache
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting; // For IHostedService
using Microsoft.Extensions.Logging; // Added for LogLevel
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Infrastructure.Repositories;
using NickScanCentralImagingPortal.ScannerServices;
using NickScanCentralImagingPortal.Services.AccessReview; // Added for access review service
using NickScanCentralImagingPortal.Services.ASE;
using NickScanCentralImagingPortal.Services.ContainerCompleteness; // Added for container completeness service
using NickScanCentralImagingPortal.Services.FS6000;
using NickScanCentralImagingPortal.Services.IcumApi;
using NickScanCentralImagingPortal.Services.ImageProcessing; // Added for image processing
using NickScanCentralImagingPortal.Services.LooseCargo; // Added for loose cargo service
using NickScanCentralImagingPortal.Services.Monitoring; // Added for performance monitoring
using NickScanCentralImagingPortal.Services.Permissions; // Added for RBAC
using NickScanCentralImagingPortal.Services.ServiceLifecycle; // Added for service lifecycle management
using NickScanCentralImagingPortal.Services.Validation; // Added for CMR validation services
using NickScanCentralImagingPortal.Services.AiWorkflow;
using NickScanCentralImagingPortal.Services.AiWorkflow.Providers;

namespace NickScanCentralImagingPortal.Services
{
    public static class ServiceConfiguration
    {
        public static IServiceCollection AddStandardizedServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddCoreServices();
            services.AddDatabaseServices(configuration);
            services.AddBackgroundServices(configuration); // Pass configuration for ASE
            services.AddHttpClients(configuration);
            services.AddEnhancedServices(configuration); // Added enhanced services

            // Runtime: Go-live date cutoff (production: only process data from this date onward)
            services.Configure<NickScanCentralImagingPortal.Core.Configuration.GoLiveOptions>(configuration.GetSection("Runtime"));
            // Data retention: purge cutoff - skip ingesting records before this date (prevents re-ingestion after purge)
            services.Configure<NickScanCentralImagingPortal.Core.Configuration.DataRetentionOptions>(configuration.GetSection("DataRetention"));
            services.Configure<AiWorkflowOptions>(configuration.GetSection(AiWorkflowOptions.SectionName));

            services.AddScoped<IAiWorkflowLineageService, AiWorkflowLineageService>();

            // AI model provider — resolves based on config
            services.AddScoped<IAiModelProvider>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<AiWorkflowOptions>>().Value;
                if (opts.ActiveProvider == "ollama-vision")
                {
                    return new AiWorkflow.Providers.OllamaVisionProvider(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient("OllamaVision"),
                        sp.GetRequiredService<ILogger<AiWorkflow.Providers.OllamaVisionProvider>>(),
                        sp.GetRequiredService<IOptions<AiWorkflowOptions>>());
                }
                if (opts.ActiveProvider == "claude-vision" && !string.IsNullOrWhiteSpace(opts.ClaudeApiKey))
                {
                    return new ClaudeVisionProvider(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient("AiVision"),
                        sp.GetRequiredService<IOptions<AiWorkflowOptions>>(),
                        sp.GetRequiredService<ILogger<ClaudeVisionProvider>>());
                }
                return new StubModelProvider();
            });

            services.AddHttpClient("OllamaVision", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(300); // Local models can be slow on CPU
            });

            services.AddHttpClient("AiVision", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(90);
            });

            services.AddScoped<IAiImageAssistService, AiImageAssistService>(sp =>
                new AiImageAssistService(
                    sp.GetRequiredService<ApplicationDbContext>(),
                    sp.GetRequiredService<IOptions<AiWorkflowOptions>>(),
                    sp.GetRequiredService<IAiModelProvider>(),
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("AiVision"),
                    sp.GetRequiredService<ILogger<AiImageAssistService>>()));
            services.AddScoped<IOpsLogTriageService, OpsLogTriageService>();
            services.AddScoped<IAiDatasetExportService, AiDatasetExportService>();
            services.AddScoped<IcumsCompletenessHintService>();

            // AI training flywheel — Gap 0: manifest snapshot at decision time
            services.AddScoped<NickScanCentralImagingPortal.Services.Manifest.IManifestSnapshotService,
                               NickScanCentralImagingPortal.Services.Manifest.ManifestSnapshotService>();

            // AI training flywheel — Gap 4: COCO export (joins decisions + annotations + manifest snapshots)
            services.AddScoped<NickScanCentralImagingPortal.Services.AiTraining.CocoExportService>();

            services.AddHostedService<AiSuggestionAutoTriggerService>();

            // Service Lifecycle Management (PRIORITY 0 - must be registered before services)
            services.Configure<ServiceLifecycleOptions>(configuration.GetSection("ServiceLifecycle"));
            services.AddSingleton<IServiceLifecycleManager, ServiceLifecycleManager>();
            services.AddHostedService<ServiceLifecycleStartupService>(); // Discovers and registers services

            // Service Orchestrator (PRIORITY 1 - manages all services)
            services.AddHostedService<ServiceOrchestratorBackgroundService>();

            // Performance Monitoring Service (PRIORITY 1 - real-time metrics)
            services.AddSingleton<PerformanceMonitoringService>();
            services.AddSingleton<IPerformanceMonitoringService>(sp => sp.GetRequiredService<PerformanceMonitoringService>());
            services.AddHostedService(sp => sp.GetRequiredService<PerformanceMonitoringService>());

            return services;
        }

        private static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Slow query interceptor for performance profiling (logs SQL > SlowQueryThresholdMs)
            services.AddSingleton<NickScanCentralImagingPortal.Infrastructure.Interceptors.SlowQueryInterceptor>();

            // Main Application Database (NS_CIS) - NO EF LOGGING, NO TRACKING BY DEFAULT
            services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                var slowQueryInterceptor = serviceProvider.GetService<NickScanCentralImagingPortal.Infrastructure.Interceptors.SlowQueryInterceptor>();
                if (slowQueryInterceptor != null)
                    options.AddInterceptors(slowQueryInterceptor);

                // NICKSCAN ERP — Phase 1 multi-tenancy: TenantOwnedEntityInterceptor
                // is a no-op for entities that don't yet implement ITenantOwned, so
                // wiring it here is safe and forward-compatible.
                var tenantInterceptor = serviceProvider.GetService<NickERP.Platform.Tenancy.TenantOwnedEntityInterceptor>();
                if (tenantInterceptor != null)
                    options.AddInterceptors(tenantInterceptor);

                // Connection-level SET app.tenant_id — drives the existing
                // tenant_isolation_* RLS policies. Before this was wired the
                // policy expression COALESCE-fell-through to '1' on every
                // request, making RLS a silent no-op. With it wired, RLS
                // actually enforces per-tenant visibility.
                var tenantConnectionInterceptor = serviceProvider.GetService<NickERP.Platform.Tenancy.TenantConnectionInterceptor>();
                if (tenantConnectionInterceptor != null)
                    options.AddInterceptors(tenantConnectionInterceptor);

                // ✅ FIX: Validate connection string with clear error message
                var connectionString = configuration.GetConnectionString("NS_CIS_Connection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Connection string 'NS_CIS_Connection' is missing or empty in configuration. Please check appsettings.json or environment variables.");
                }
                options.UseNpgsql(connectionString, npgOptions =>
                {
                    npgOptions.CommandTimeout(configuration.GetValue<int>("Database:CommandTimeoutSeconds", 120));
                    npgOptions.EnableRetryOnFailure(
                        configuration.GetValue<int>("Database:MaxRetryCount", 3),
                        TimeSpan.FromSeconds(configuration.GetValue<int>("Database:MaxRetryDelaySeconds", 5)),
                        null);
                });
                options.EnableSensitiveDataLogging(false);
                options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            });

            // ICUMS Database - NO EF LOGGING, NO TRACKING BY DEFAULT
            services.AddDbContext<IcumDbContext>((serviceProvider, options) =>
            {
                var slowQueryInterceptor = serviceProvider.GetService<NickScanCentralImagingPortal.Infrastructure.Interceptors.SlowQueryInterceptor>();
                if (slowQueryInterceptor != null)
                    options.AddInterceptors(slowQueryInterceptor);

                var tenantInterceptor = serviceProvider.GetService<NickERP.Platform.Tenancy.TenantOwnedEntityInterceptor>();
                if (tenantInterceptor != null)
                    options.AddInterceptors(tenantInterceptor);

                // Connection-level SET app.tenant_id. icums tables don't yet
                // carry tenant_id (Phase-2 work) so the setting is a harmless
                // no-op today, but wiring it here keeps every DbContext on
                // the same contract for when icums tables join the policy.
                var tenantConnectionInterceptor = serviceProvider.GetService<NickERP.Platform.Tenancy.TenantConnectionInterceptor>();
                if (tenantConnectionInterceptor != null)
                    options.AddInterceptors(tenantConnectionInterceptor);

                var connectionString = configuration.GetConnectionString("ICUMS_Connection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Connection string 'ICUMS_Connection' is missing or empty in configuration. Please check appsettings.json or environment variables.");
                }
                options.UseNpgsql(connectionString, npgOptions =>
                {
                    npgOptions.CommandTimeout(configuration.GetValue<int>("Database:CommandTimeoutSeconds", 120));
                    npgOptions.EnableRetryOnFailure(
                        configuration.GetValue<int>("Database:MaxRetryCount", 3),
                        TimeSpan.FromSeconds(configuration.GetValue<int>("Database:MaxRetryDelaySeconds", 5)),
                        null);
                });
                options.EnableSensitiveDataLogging(false);
                options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            });

            // ICUMS Downloads Database - NO EF LOGGING, NO TRACKING BY DEFAULT
            services.AddDbContext<IcumDownloadsDbContext>((serviceProvider, options) =>
            {
                var slowQueryInterceptor = serviceProvider.GetService<NickScanCentralImagingPortal.Infrastructure.Interceptors.SlowQueryInterceptor>();
                if (slowQueryInterceptor != null)
                    options.AddInterceptors(slowQueryInterceptor);

                var tenantInterceptor = serviceProvider.GetService<NickERP.Platform.Tenancy.TenantOwnedEntityInterceptor>();
                if (tenantInterceptor != null)
                    options.AddInterceptors(tenantInterceptor);

                // Connection-level SET app.tenant_id (same forward-compat
                // wiring as the IcumDbContext above).
                var tenantConnectionInterceptor = serviceProvider.GetService<NickERP.Platform.Tenancy.TenantConnectionInterceptor>();
                if (tenantConnectionInterceptor != null)
                    options.AddInterceptors(tenantConnectionInterceptor);

                var connectionString = configuration.GetConnectionString("ICUMS_Downloads_Connection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Connection string 'ICUMS_Downloads_Connection' is missing or empty in configuration. Please check appsettings.json or environment variables.");
                }
                options.UseNpgsql(connectionString, npgOptions =>
                {
                    npgOptions.CommandTimeout(configuration.GetValue<int>("Database:CommandTimeoutSeconds", 120));
                    npgOptions.EnableRetryOnFailure(
                        configuration.GetValue<int>("Database:MaxRetryCount", 3),
                        TimeSpan.FromSeconds(configuration.GetValue<int>("Database:MaxRetryDelaySeconds", 5)),
                        null);
                });
                options.EnableSensitiveDataLogging(false);
                options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            });

            return services;
        }

        private static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            // ✅ FIX: Register IHttpContextAccessor for services that need HTTP context
            services.AddHttpContextAccessor();

            // Repositories
            services.AddScoped<IContainerRepository, ContainerRepository>();
            services.AddScoped<IContainerImageRepository, ContainerImageRepository>();
            services.AddScoped<IProcessingResultRepository, ProcessingResultRepository>();
            services.AddScoped<IIcumRepository, IcumRepository>();
            services.AddScoped<IIcumDownloadsRepository, IcumDownloadsRepository>();
            services.AddScoped<ICrossRecordScanRepository, CrossRecordScanRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IScannerRepository, ScannerRepository>(); // Added Scanner Repository
            services.AddScoped<IVehicleImportRepository, VehicleImportRepository>(); // Added Vehicle Import Repository
            services.AddScoped<IICUMSDownloadQueueRepository, ICUMSDownloadQueueRepository>(); // ICUMS Download Queue Repository
            services.AddScoped<IBLReviewRepository, BLReviewRepository>(); // BL Review Repository
            services.AddScoped<IContainerProcessingRepository, ContainerProcessingRepository>(); // Container Processing Repository
            services.AddScoped<ILooseCargoRepository, LooseCargoRepository>(); // Loose Cargo Repository

            // Shift & Attendance Repositories
            services.AddScoped<IShiftTemplateRepository, ShiftTemplateRepository>();
            services.AddScoped<IShiftAssignmentRepository, ShiftAssignmentRepository>();
            services.AddScoped<IAttendanceRecordRepository, AttendanceRecordRepository>();
            services.AddScoped<IShiftCoverageRepository, ShiftCoverageRepository>();
            services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();

            // ? MEMORY FIX: Changed from Singleton to Scoped to prevent DbContext accumulation
            // Background services will create scopes per operation instead of holding references forever
            services.AddScoped<IContainerDataRepository, NickScanCentralImagingPortal.Infrastructure.Repositories.ContainerDataRepository>();

            // Container Scan Queue Repository (for queue-based completeness processing)
            services.AddScoped<IContainerScanQueueRepository, NickScanCentralImagingPortal.Infrastructure.Repositories.ContainerScanQueueRepository>();

            // Container Scan Queue Publisher Service (unified abstraction for all scanners)
            // Future-proof: Any scanner can inject this service and publish scans without code changes
            services.AddScoped<IContainerScanQueuePublisher, NickScanCentralImagingPortal.Services.ContainerCompleteness.ContainerScanQueuePublisherService>();

            // Queue publishing metrics and alerting
            services.AddSingleton<NickScanCentralImagingPortal.Services.ContainerCompleteness.QueuePublishingMetricsService>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ContainerCompleteness.QueuePublishingAlertService>();

            // Container completeness pipeline (scoped services for API injection)
            services.AddScoped<IContainerCompletenessService, NickScanCentralImagingPortal.Services.ContainerCompleteness.ContainerCompletenessService>();
            services.AddScoped<IManualBOESelectivityService, ManualBOESelectivityService>();
            services.AddScoped<IContainerDataMapperService, ContainerDataMapperService>();

            // Container Completeness Orchestrator (background service coordinating the pipeline)
            services.AddHostedService<NickScanCentralImagingPortal.Services.ContainerCompleteness.ContainerCompletenessOrchestratorService>();

            // ✅ QUEUE RECOVERY SERVICE: Recovers missed scans from scanner tables (ultimate safety net)
            services.AddHostedService<NickScanCentralImagingPortal.Services.ContainerCompleteness.QueueRecoveryService>();

            // CMR Validation and Re-download Services
            services.AddScoped<ICMRValidationService, CMRValidationService>();
            services.AddScoped<ICMRRedownloadService, CMRRedownloadService>();
            services.AddHostedService<CMRRedownloadBackgroundService>();
            services.AddHostedService<CMRMetricsRecorderService>();

            // ✅ ISO 27001: Access Review Service (quarterly reviews)
            services.AddHostedService<AccessReview.AccessReviewService>();

            // Email Service
            services.AddScoped<IEmailService, NickScanCentralImagingPortal.Services.Email.EmailService>();
            services.AddHostedService<NickScanCentralImagingPortal.Services.Email.DailyDataQualityReportService>();

            // NickComms Gateway client (typed HttpClient)
            services.AddHttpClient<INickCommsClient, NickScanCentralImagingPortal.Services.Email.NickCommsClient>(client =>
            {
                // BaseAddress and X-Api-Key are set per-call inside the client from ISettingsProvider,
                // so the admin settings page can update them at runtime without restart.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            // 2026-04-27: forward X-Correlation-ID across the API → Gateway boundary so
            // ops can grep a single ID through both services' logs.
            .AddHttpMessageHandler<NickScanCentralImagingPortal.Services.Http.CorrelationForwardingHandler>();

            // Settings Management Services
            services.AddScoped<ISettingsRepository, SettingsRepository>();
            services.AddScoped<ISettingsEncryptionService, NickScanCentralImagingPortal.Services.Settings.SettingsEncryptionService>();
            services.AddScoped<ISettingsValidationService, NickScanCentralImagingPortal.Services.Settings.SettingsValidationService>();
            services.AddScoped<ISettingsService, NickScanCentralImagingPortal.Services.Settings.SettingsService>();
            services.AddScoped<ISettingsProvider, NickScanCentralImagingPortal.Services.Settings.SettingsProvider>(); // Centralized settings access

            // Category-specific configuration providers
            services.AddScoped<NickScanCentralImagingPortal.Services.Settings.ICUMSConfigurationProvider>();
            services.AddScoped<NickScanCentralImagingPortal.Services.Settings.SecurityConfigurationProvider>();
            services.AddScoped<NickScanCentralImagingPortal.Services.Settings.ScannerConfigurationProvider>();
            services.AddScoped<NickScanCentralImagingPortal.Services.Settings.PerformanceConfigurationProvider>();
            services.AddScoped<NickScanCentralImagingPortal.Services.Settings.RateLimitingConfigurationProvider>();
            services.AddScoped<NickScanCentralImagingPortal.Services.Settings.CachingConfigurationProvider>();

            // ICUMS Submission Service
            services.AddSingleton<ICUMSSubmissionService>();
            services.AddSingleton<IICUMSSubmissionService>(sp => sp.GetRequiredService<ICUMSSubmissionService>());
            services.AddHostedService(sp => sp.GetRequiredService<ICUMSSubmissionService>());

            // Enhanced Container Validation Services
            services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.IClearanceTypeDetectionService, NickScanCentralImagingPortal.Services.ContainerValidation.ClearanceTypeDetectionService>();
            services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.IContainerValidationService, NickScanCentralImagingPortal.Services.ContainerValidation.ContainerValidationService>();
            services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.IScannerDataValidationService, NickScanCentralImagingPortal.Services.ContainerValidation.ScannerDataValidationService>();

            // Cargo Summary Service (local template-based summarization)
            services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.ICargoSummaryService, NickScanCentralImagingPortal.Services.CargoGrouping.CargoSummaryService>();
            services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.IImageValidationService, NickScanCentralImagingPortal.Services.ContainerValidation.ImageValidationService>();
            // ? MEMORY FIX: Changed from Singleton to Scoped to prevent memory leaks
            // Cache service should use IMemoryCache (which is already singleton) instead of holding data directly
            services.AddScoped<NickScanCentralImagingPortal.Services.ContainerValidation.IICUMSDataCacheService, NickScanCentralImagingPortal.Services.ContainerValidation.ICUMSDataCacheService>();

            // Vehicle Import Services
            services.AddScoped<IVehicleImportService, NickScanCentralImagingPortal.Services.VehicleImport.VehicleImportService>();

            // Loose Cargo Services
            services.AddScoped<ILooseCargoService, LooseCargoService>();

            // Role-Based Access Control Services
            services.AddScoped<IPermissionService, PermissionService>();
            services.AddScoped<IRoleService, RoleService>();
            services.AddScoped<IPermissionCatalogBuilder, PermissionCatalogBuilder>();
            services.AddScoped<NickScanCentralImagingPortal.Services.Audit.IAuditService, NickScanCentralImagingPortal.Services.Audit.AuditService>();

            // Shared cache for ready groups queries (used by AssignmentWorker, orchestrator)
            services.AddSingleton<NickScanCentralImagingPortal.Services.ImageAnalysis.ReadyGroupsCacheService>();
            // Post-decision side effects (records, assignments, workflow sync)
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionSideEffectsService>();

            // Monitoring infrastructure
            services.AddSingleton<NickScanCentralImagingPortal.Services.Monitoring.AdaptivePollingHelper>();
            services.AddSingleton<NickScanCentralImagingPortal.Services.Monitoring.ServiceHealthMonitor>();

            // JWT Authentication Service
            services.AddScoped<IJwtService, NickScanCentralImagingPortal.Services.Authentication.JwtService>();

            // Shift & Attendance Services
            services.AddScoped<IShiftTemplateService, NickScanCentralImagingPortal.Services.ShiftAttendance.ShiftTemplateService>();
            services.AddScoped<IShiftAssignmentService, NickScanCentralImagingPortal.Services.ShiftAttendance.ShiftAssignmentService>();
            services.AddScoped<IAttendanceService, NickScanCentralImagingPortal.Services.ShiftAttendance.AttendanceService>();
            services.AddScoped<IShiftCoverageService, NickScanCentralImagingPortal.Services.ShiftAttendance.ShiftCoverageService>();

            // Comprehensive Dashboard Service
            services.AddScoped<NickScanCentralImagingPortal.Services.Dashboard.IComprehensiveDashboardService, NickScanCentralImagingPortal.Services.Dashboard.ComprehensiveDashboardService>();

            // Scanner Services
            services.AddScoped<NickScanCentralImagingPortal.ScannerServices.Nuctech.NuctechScannerService>();
            services.AddScoped<NickScanCentralImagingPortal.ScannerServices.HeimannSmith.HeimannSmithScannerService>();
            services.AddScoped<IScannerServiceFactory, ScannerServiceFactory>();
            services.AddScoped<IImageProcessingOrchestrator, ImageProcessingOrchestrator>(); // Corrected type

            // Image Processing Services - Using fully qualified names to avoid ambiguity
            services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.IImageProcessingService, NickScanCentralImagingPortal.Services.ImageProcessing.ImageProcessingService>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.IImageCacheService, NickScanCentralImagingPortal.Services.ImageProcessing.ImageCacheService>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.ASE.IASEImageConverterService, NickScanCentralImagingPortal.Services.ImageProcessing.ASE.ASEImageConverterService>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.IImageAnnotationRenderer, NickScanCentralImagingPortal.Services.ImageProcessing.ImageAnnotationRenderer>();
            services.AddScoped<FS6000ImagePipeline>();
            services.AddScoped<ASEImagePipeline>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.FS6000RawChannelIngester>();

            // v2.11.0 — unified scan processing kernel.
            //   IScanFormatAdapter: one per wire format (byte layout parser)
            //   IScanSourceRetriever: one per data source (DB reader)
            //   ScannerTypeDetector: container → scanner type
            //   ScanRouter: container → IR (decode + 30s cache)
            //   ScanProcessingPipeline: the single public orchestrator
            // Adding a new scanner = register a new IScanFormatAdapter +
            // IScanSourceRetriever here. No changes to the pipeline or kernel.
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions.IScanFormatAdapter,
                               NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Adapters.FS6000FormatAdapter>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions.IScanFormatAdapter,
                               NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Adapters.ASEFormatAdapter>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions.IScanSourceRetriever,
                               NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Retrievers.FS6000SourceRetriever>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions.IScanSourceRetriever,
                               NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Retrievers.ASESourceRetriever>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.IScannerTypeDetector,
                               NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.ScannerTypeDetector>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.ScanRouter>();
            services.AddScoped<NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.ScanProcessingPipeline>();
            services.AddSingleton<NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.FS6000BackfillJobTracker>();
            // 2026-04-19: worker closes the gap between fs6000scans and raw-channel rows
            // every 5 min. "Never abandon" — a scan stays in its working set until all
            // three channels are in fs6000images or it ages out of the 7-day window.
            services.AddHostedService<NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.FS6000RawChannelBackfillWorker>();

            // v2.9.6: typed HttpClient for the Python inspector's /composite/ endpoint.
            // Used by FS6000ImagePipeline to render 16-bit composites from DB-ingested
            // raw channels. Base URL matches the Raw Image Engine (NSCIM_ImageSplitter).
            services.AddHttpClient<NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.FS6000CompositeProxyClient>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:5320/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddScoped<ICUMSIntegrationService>();
            // Image processing pipeline services
            services.AddScoped<IAdvancedImageProcessingService, NickScanCentralImagingPortal.Services.ImageProcessing.AdvancedImageProcessingService>();
            services.AddScoped<IContainerNumberOcrService, NickScanCentralImagingPortal.Services.ImageProcessing.ContainerNumberOcrService>();
            services.AddScoped<IContainerObjectDetectionService, NickScanCentralImagingPortal.Services.ImageProcessing.ContainerObjectDetectionService>();
            services.AddScoped<IImageQualityAssessmentService, NickScanCentralImagingPortal.Services.ImageProcessing.ImageQualityAssessmentService>();
            // Facade: wraps all image analysis services behind one interface
            services.AddScoped<IImageAnalysisFacade, NickScanCentralImagingPortal.Services.ImageProcessing.ImageAnalysisFacade>();

            // Gateway Orchestration Service - Phase 1 (Hybrid Smart Gateway)
            services.AddScoped<IGatewayOrchestrationService, NickScanCentralImagingPortal.Services.Gateway.GatewayOrchestrationService>();

            // Dashboard Stats Service - Phase 2
            services.AddScoped<IDashboardStatsService, NickScanCentralImagingPortal.Services.Gateway.DashboardStatsService>();

            // Global Search Service - Phase 2
            services.AddScoped<IGlobalSearchService, NickScanCentralImagingPortal.Services.Gateway.GlobalSearchService>();

            // Report Generation Service - Phase 2
            services.AddScoped<IReportGenerationService, NickScanCentralImagingPortal.Services.Gateway.ReportGenerationService>();

            // Batch Operation Service - Phase 2
            services.AddScoped<IBatchOperationService, NickScanCentralImagingPortal.Services.Gateway.BatchOperationService>();

            return services;
        }

        private static IServiceCollection AddBackgroundServices(this IServiceCollection services, IConfiguration configuration)
        {
            // ASE Services (ENABLED - Priority 1)
            services.Configure<AseConfiguration>(configuration.GetSection("ASE"));
            // ✅ Post-configure to replace password placeholder with environment variable
            services.PostConfigure<AseConfiguration>(config =>
            {
                if (!string.IsNullOrEmpty(config.ConnectionString) &&
                    (config.ConnectionString.Contains("***USE_ENV_VAR") || config.ConnectionString.Contains("***USE_ENV")))
                {
                    var asePassword = Environment.GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD");
                    if (!string.IsNullOrEmpty(asePassword))
                    {
                        var originalConnString = config.ConnectionString;
                        // Escape special characters in password for SQL connection string (; and =)
                        var escapedPassword = asePassword.Replace(";", ";;").Replace("=", "==");
                        config.ConnectionString = config.ConnectionString
                            .Replace("***USE_ENV_VAR_NICKSCAN_ASE_PASSWORD***", escapedPassword)
                            .Replace("***USE_ENV_VAR***", escapedPassword)
                            .Replace("***USE_ENV***", escapedPassword);

                        if (config.ConnectionString != originalConnString)
                        {
                            // Log success (but don't log the actual password)
                            var loggerFactory = services.BuildServiceProvider().GetService<ILoggerFactory>();
                            var logger = loggerFactory?.CreateLogger("AseConfiguration");
                            logger?.LogInformation("✅ [PostConfigure] ASE connection string password replaced from environment variable");
                        }
                    }
                    else
                    {
                        var loggerFactory = services.BuildServiceProvider().GetService<ILoggerFactory>();
                        var logger = loggerFactory?.CreateLogger("AseConfiguration");
                        logger?.LogWarning("⚠️ [PostConfigure] ASE connection string contains password placeholder but NICKSCAN_ASE_PASSWORD environment variable is not set");
                    }
                }
            });
            services.AddScoped<IAseDatabaseSyncService, AseDatabaseSyncService>();
            services.AddHostedService<AseBackgroundService>();

            // ICUMS Services (ENABLED - Priority 2)
            services.AddScoped<IIcumApiService, IcumApiService>();

            // ICUMS metrics (singleton, shared across pipeline)
            services.AddSingleton<ICUMSMetrics>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger<ICUMSMetrics>();
                return new ICUMSMetrics(logger);
            });

            // ICUMS Pipeline Orchestrator (consolidates IcumBackgroundService, IcumFileScannerService,
            // IcumJsonIngestionService, IcumDataTransferService, ICUMSDownloadBackgroundService into one)
            services.AddHostedService<IcumPipelineOrchestratorService>();

            // ICUMS Download Queue Service (still needed as scoped service for API calls)
            services.AddScoped<IICUMSDownloadQueueService, ICUMSDownloadQueueService>();

            // FS6000 Services (ENABLED - Priority 3) - FIXED: Added XmlParsingService
            services.Configure<FileSyncConfiguration>(configuration.GetSection("FS6000:FileSync"));
            services.Configure<IngestionConfiguration>(configuration.GetSection("FS6000:Ingestion"));
            services.AddScoped<IXmlParsingService, XmlParsingService>(); // FIXED: Added this line
            services.AddScoped<IFileSyncService, FileSyncService>();
            services.AddScoped<IIngestionService, IngestionService>();
            services.AddScoped<IFS6000ImageCompletenessService, FS6000ImageCompletenessService>(); // Image completeness validation
            services.AddHostedService<FS6000BackgroundService>();

            // Image Analysis Orchestrator (consolidates intake, assignment, submission, decision agent, housekeeping workers)
            services.AddHostedService<NickScanCentralImagingPortal.Services.ImageAnalysis.ImageAnalysisOrchestratorService>();

            // Zombie AnalysisGroup sweeper — archives AnalystCompleted groups with zero CCS rows
            // after a grace window. Prevents the SLA banner and audit-assignment queue from being
            // poisoned by groups that the audit pipeline's "all containers in Audit stage" gate
            // passes vacuously. See the service's class-level doc for context.
            services.AddHostedService<NickScanCentralImagingPortal.Services.ImageAnalysis.ZombieAnalysisGroupSweeperService>();

            // ✅ DEFENSIVE MONITORING SERVICES (6-Layer Defense System)
            // These services provide active monitoring and reconciliation to prevent data issues
            services.AddHostedService<NickScanCentralImagingPortal.Services.Monitoring.DuplicateDownloadMonitoringService>();
            services.AddHostedService<NickScanCentralImagingPortal.Services.ContainerCompleteness.ContainerStatusReconciliationService>();

            // Endpoint Usage Monitoring Cleanup Service
            // Periodically cleans up old endpoint usage logs (keeps 90 days)
            services.AddHostedService<NickScanCentralImagingPortal.Services.Monitoring.EndpointUsageCleanupBackgroundService>();

            // Error Investigation System Services
            services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.IErrorInvestigationService,
                NickScanCentralImagingPortal.Services.Monitoring.ErrorInvestigationService>();
            services.AddScoped<NickScanCentralImagingPortal.Core.Interfaces.IFixImplementationService,
                NickScanCentralImagingPortal.Services.Monitoring.FixImplementationService>();
            services.AddHostedService<NickScanCentralImagingPortal.Services.Monitoring.ErrorMonitoringBackgroundService>();

            return services;
        }

        private static IServiceCollection AddEnhancedServices(this IServiceCollection services, IConfiguration configuration)
        {
            // ? REMOVED: Duplicate MemoryCache (already configured in Program.cs with SizeLimit=1000)
            // This was creating a second cache instance, doubling memory overhead (~200 MB saved)

            // ✅ PERFORMANCE FIX: Register as Singleton to prevent repeated initialization
            // Backup service only needs to initialize directory structure once
            services.AddSingleton<IIcumBackupService, IcumBackupService>();

            // Configure enhanced ICUMS settings
            services.Configure<IcumEnhancedConfiguration>(configuration.GetSection("ICUMS:EnhancedSettings"));

            // Reports Services
            services.AddScoped<IReportsService, NickScanCentralImagingPortal.Services.Reports.ReportsService>();

            return services;
        }

        private static IServiceCollection AddHttpClients(this IServiceCollection services, IConfiguration configuration)
        {
            // ICUMS HTTP Client with Proxy Support and Enhanced Configuration
            services.AddHttpClient<IIcumApiService, IcumApiService>((serviceProvider, client) =>
            {
                var baseUrl = configuration["ICUMS:BaseUrl"] ?? "http://localhost:5205";
                // ✅ FIX: Increase default timeout to 600 seconds (10 minutes) to match appsettings.json
                // This prevents timeout errors for large batch downloads
                var timeout = int.Parse(configuration["ICUMS:TimeoutSeconds"] ?? "600");

                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(timeout);
                client.DefaultRequestHeaders.Add("User-Agent", "NickScan-Central-Imaging-Portal/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler((serviceProvider) =>
            {
                var config = serviceProvider.GetRequiredService<IConfiguration>();
                var logger = serviceProvider.GetRequiredService<ILogger<IcumApiService>>();
                var handler = new HttpClientHandler();

                var proxyEnabled = bool.Parse(config["ICUMS:Proxy:Enabled"] ?? "false");
                var proxyAddress = config["ICUMS:Proxy:Address"];

                if (proxyEnabled && !string.IsNullOrEmpty(proxyAddress))
                {
                    try
                    {
                        var proxy = new System.Net.WebProxy(proxyAddress)
                        {
                            BypassProxyOnLocal = bool.Parse(config["ICUMS:Proxy:BypassOnLocal"] ?? "false")
                        };

                        var username = config["ICUMS:Proxy:Username"];
                        var password = config["ICUMS:Proxy:Password"];
                        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                        {
                            proxy.Credentials = new System.Net.NetworkCredential(username, password);
                            logger.LogInformation("[ICUMS-PROXY] Proxy configured with authentication: {ProxyAddress}", proxyAddress);
                        }
                        else
                        {
                            logger.LogInformation("[ICUMS-PROXY] Proxy configured without authentication: {ProxyAddress}", proxyAddress);
                        }

                        handler.Proxy = proxy;
                        handler.UseProxy = true;

                        // ✅ FIX: Set connection timeout for proxy connections
                        // This helps detect unreachable proxies faster
                        handler.MaxConnectionsPerServer = 10;

                        logger.LogInformation("[ICUMS-PROXY] ✅ Proxy enabled: {ProxyAddress} (BypassOnLocal: {BypassOnLocal})",
                            proxyAddress, proxy.BypassProxyOnLocal);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[ICUMS-PROXY] ❌ Failed to configure proxy: {ProxyAddress}. Requests will fail.", proxyAddress);
                        // Continue without proxy - let the error surface naturally
                    }
                }
                else
                {
                    handler.UseProxy = false;
                    logger.LogDebug("[ICUMS-PROXY] Proxy disabled - using direct connection");
                }

                return handler;
            });

            // Raw Image Engine (Python service — decoding, rendering, analysis, splitting)
            var rawImageEngineUrl = configuration.GetValue<string>("RawImageEngine:BaseUrl")
                ?? configuration.GetValue<string>("ImageSplitter:BaseUrl")
                ?? "http://localhost:5320";
            services.AddHttpClient<NickScanCentralImagingPortal.Services.ImageSplitter.IImageSplitterService,
                NickScanCentralImagingPortal.Services.ImageSplitter.ImageSplitterService>((serviceProvider, client) =>
            {
                client.BaseAddress = new Uri(rawImageEngineUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "NickScan-NSCIM/2.6");
            });

            services.AddHttpClient("RawImageEngine", (serviceProvider, client) =>
            {
                client.BaseAddress = new Uri(rawImageEngineUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            return services;
        }
    }

    // Configuration class for enhanced ICUMS settings
    public class IcumEnhancedConfiguration
    {
        public CircuitBreakerSettings CircuitBreaker { get; set; } = new();
        public RetryPolicySettings RetryPolicy { get; set; } = new();
        public CachingSettings Caching { get; set; } = new();
        public BackupSettings Backup { get; set; } = new();
        public HealthMonitoringSettings HealthMonitoring { get; set; } = new();
        public MetricsSettings Metrics { get; set; } = new();
    }

    public class CircuitBreakerSettings
    {
        public int FailureThreshold { get; set; } = 5;
        public int TimeoutMinutes { get; set; } = 5;
        public int HalfOpenMaxCalls { get; set; } = 3;
    }

    public class RetryPolicySettings
    {
        public int MaxRetries { get; set; } = 3;
        public int BaseDelaySeconds { get; set; } = 1;
        public int MaxDelaySeconds { get; set; } = 30;
        public bool ExponentialBackoff { get; set; } = true;
    }

    public class CachingSettings
    {
        public bool Enabled { get; set; } = true;
        public int DefaultTimeoutMinutes { get; set; } = 10;
        public int ApiStatusTimeoutMinutes { get; set; } = 5;
        public int ContainerDataTimeoutMinutes { get; set; } = 15;
    }

    public class BackupSettings
    {
        public bool Enabled { get; set; } = true;
        public string Directory { get; set; } = @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Backup";
        public int MaxFileSizeMB { get; set; } = 50;
        public int RetentionDays { get; set; } = 30;
        public bool CompressionEnabled { get; set; } = false;
    }

    public class HealthMonitoringSettings
    {
        public bool Enabled { get; set; } = true;
        public int CheckIntervalMinutes { get; set; } = 2;
        public int TimeoutSeconds { get; set; } = 30;
        public int FailureThreshold { get; set; } = 3;
    }

    public class MetricsSettings
    {
        public bool Enabled { get; set; } = true;
        public int RetentionHours { get; set; } = 24;
        public double SamplingRate { get; set; } = 1.0;
    }
}
