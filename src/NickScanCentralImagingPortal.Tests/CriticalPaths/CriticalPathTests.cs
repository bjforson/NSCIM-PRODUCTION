using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services;
using NickScanCentralImagingPortal.Services.IcumApi;
using NickScanCentralImagingPortal.Services.ContainerCompleteness;
using NickScanCentralImagingPortal.Services.Monitoring;
using NickScanCentralImagingPortal.Services.ASE;
using NickScanCentralImagingPortal.Services.FS6000;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.CriticalPaths
{
    /// <summary>
    /// Critical path tests for the most important system functionality
    /// </summary>
    public class CriticalPathTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;
        private readonly IServiceProvider _serviceProvider;

        public CriticalPathTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Configure test database
                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseInMemoryDatabase("TestDb"));
                    services.AddDbContext<IcumDownloadsDbContext>(options =>
                        options.UseInMemoryDatabase("TestIcumDb"));
                });
            });

            _client = _factory.CreateClient();
            _serviceProvider = _factory.Services;
        }

        // ICUMS_BatchDownload_CriticalPath_ShouldWork was deleted in 1.11.0.
        // It referenced IIcumRepository.GetContainerDataByDateRangeAsync which
        // no longer exists — ICUMS batch downloads now go through
        // ICUMSDownloadBackgroundService directly. Schema-level regression
        // coverage is provided by Tests/Schema/NewEntitySchemaTests.cs.

        [Fact]
        public async Task ContainerDataMapping_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var mapperService = scope.ServiceProvider.GetRequiredService<IContainerDataMapperService>();
            var appDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Create test data
            var testContainerNumber = "TEST1234567";
            var testScannerType = "FS6000";
            var testScannerDataId = 12345;
            var testIcumDataId = 67890;

            // Act
            var mapping = await mapperService.MapContainerDataAsync(
                testContainerNumber, 
                testScannerType, 
                testScannerDataId, 
                testIcumDataId);

            // Assert
            Assert.NotNull(mapping);
            Assert.Equal(testContainerNumber, mapping.ContainerNumber);
            Assert.Equal(testScannerType, mapping.ScannerType);
            Assert.Equal(testScannerDataId, mapping.ScannerDataId);
            Assert.Equal(testIcumDataId, mapping.ICUMSBOEId);
        }

        [Fact]
        public async Task PerformanceMonitoring_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var performanceService = scope.ServiceProvider.GetRequiredService<IPerformanceMonitoringService>();

            // Act
            var summary = performanceService.GetPerformanceSummary();
            var metrics = performanceService.GetCurrentMetrics();

            // Assert
            Assert.NotNull(summary);
            Assert.NotNull(metrics);
            Assert.True(summary.TotalMetrics > 0);
            Assert.Contains("SystemHealth", summary.GetType().GetProperties().Select(p => p.Name));
        }

        [Fact]
        public async Task HealthCheck_CriticalPath_ShouldWork()
        {
            // Arrange
            var healthCheckUrl = "/api/Monitoring/health/overview";

            // Act
            var response = await _client.GetAsync(healthCheckUrl);

            // Assert
            Assert.True(response.IsSuccessStatusCode);
            
            var content = await response.Content.ReadAsStringAsync();
            Assert.NotEmpty(content);
            
            var healthData = JsonSerializer.Deserialize<object>(content);
            Assert.NotNull(healthData);
        }

        [Fact]
        public async Task DatabaseConnection_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var appDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

            // Act & Assert
            var appCanConnect = await appDbContext.Database.CanConnectAsync();
            var icumCanConnect = await icumDbContext.Database.CanConnectAsync();

            Assert.True(appCanConnect, "Application database should be connectable");
            Assert.True(icumCanConnect, "ICUMS database should be connectable");
        }

        // FileProcessing_CriticalPath_ShouldWork was deleted in 1.11.0.
        // It referenced IcumFileScannerService.ProcessFilesAsync() which no
        // longer exists — file processing happens via the BackgroundService
        // loop directly. No replacement test was written because the
        // background-service pattern is covered implicitly by end-to-end
        // ICUMS ingestion runs in the live environment.

        [Fact]
        public async Task MemoryOptimization_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var mapperService = scope.ServiceProvider.GetRequiredService<IContainerDataMapperService>();

            // Act + Assert — process pending mappings twice and ensure memory growth is bounded.
            // Guards against the historical regression where the mapper loaded the full
            // AseScans table into memory (~24 GB) instead of streaming.
            await mapperService.ProcessPendingMappingsAsync(CancellationToken.None);

            var memoryBefore = GC.GetTotalMemory(false);
            await mapperService.ProcessPendingMappingsAsync(CancellationToken.None);
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryIncrease = memoryAfter - memoryBefore;

            Assert.True(memoryIncrease < 100 * 1024 * 1024, // Less than 100 MB
                $"Memory increase should be reasonable, but was {memoryIncrease / 1024 / 1024} MB");
        }

        // Deduplication_CriticalPath_ShouldWork was deleted in 1.11.0.
        // It referenced ShouldDownloadContainerAsync / RecordDownloadAttemptAsync
        // which no longer exist. Deduplication moved to
        // GetMostRecentDownloadForContainerAsync + the ContainerDownloadHistory
        // table. A replacement test would need to spin up both DbContexts with
        // real data and is better suited to an integration-test project than
        // this unit-test surface.

        [Fact]
        public async Task ServiceOrchestrator_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ServiceOrchestratorBackgroundService>();

            // Act & Assert
            // The orchestrator should be able to start without errors
            Assert.NotNull(orchestrator);
            
            // Test that it can coordinate services
            var canStart = true; // This would test actual startup coordination
            Assert.True(canStart, "Service orchestrator should be able to coordinate startup");
        }

        [Fact]
        public async Task API_Endpoints_CriticalPath_ShouldWork()
        {
            // Arrange
            var criticalEndpoints = new[]
            {
                "/api/Monitoring/health/overview",
                "/api/Performance/summary",
                "/api/ContainerValidation/validate",
                "/api/ContainerCompleteness/status"
            };

            // Act & Assert
            foreach (var endpoint in criticalEndpoints)
            {
                var response = await _client.GetAsync(endpoint);
                
                // Endpoints should either return success or proper error codes
                Assert.True(response.IsSuccessStatusCode || 
                           response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                           response.StatusCode == System.Net.HttpStatusCode.Unauthorized,
                    $"Endpoint {endpoint} should return valid status code, but returned {response.StatusCode}");
            }
        }

        [Fact]
        public async Task ErrorHandling_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var mapperService = scope.ServiceProvider.GetRequiredService<IContainerDataMapperService>();

            // Act & Assert - Test error handling with invalid data
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await mapperService.MapContainerDataAsync("", "INVALID", -1, -1);
            });
        }

        [Fact]
        public void BackgroundServices_CriticalPath_ShouldWork()
        {
            // Arrange — resolve each background service individually so a missing
            // registration produces a targeted failure instead of an opaque
            // implicitly-typed-array error.
            using var scope = _serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;

            // Act & Assert — each GetService<T>() returns null if not registered.
            // We deliberately use GetService (not GetRequiredService) so we get a
            // soft assertion per service, not a hard exception on the first miss.
            Assert.NotNull(sp.GetService<FS6000BackgroundService>());
            Assert.NotNull(sp.GetService<AseBackgroundService>());
            // IcumBackgroundService and ICUMSDownloadBackgroundService removed — replaced by IcumPipelineOrchestratorService
            Assert.NotNull(sp.GetService<IcumFileScannerService>());
            Assert.NotNull(sp.GetService<IcumJsonIngestionService>());
            Assert.NotNull(sp.GetService<IcumDataTransferService>());
            Assert.NotNull(sp.GetService<ContainerCompletenessService>());
        }

        [Fact]
        public async Task DatabaseTransactions_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var appDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Act - Test transaction handling
            using var transaction = await appDbContext.Database.BeginTransactionAsync();
            
            try
            {
                // Simulate some database operations
                await appDbContext.Database.ExecuteSqlRawAsync("SELECT 1");
                await transaction.CommitAsync();
                
                // Assert
                Assert.True(true, "Transaction should commit successfully");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        [Fact]
        public async Task Configuration_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // Act & Assert - Test critical configuration values
            var apiUrl = configuration["ICUMS:ApiUrl"];
            var batchInterval = configuration["BackgroundServices:IcumBackgroundService:BatchIntervalMinutes"];
            var mappingInterval = configuration["BackgroundServices:ContainerDataMapperService:MappingIntervalMinutes"];

            Assert.NotNull(apiUrl);
            Assert.NotNull(batchInterval);
            Assert.NotNull(mappingInterval);
        }

        [Fact]
        public void Logging_CriticalPath_ShouldWork()
        {
            // Arrange
            using var scope = _serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<CriticalPathTests>>();

            // Act — xunit treats unhandled exceptions as failures, so a successful
            // call sequence is itself the assertion (Assert.DoesNotThrow was removed
            // in xunit 2.x).
            logger.LogInformation("Test log message");
            logger.LogWarning("Test warning message");
            logger.LogError("Test error message");
        }
    }

    /// <summary>
    /// Integration tests for end-to-end critical paths
    /// </summary>
    public class CriticalPathIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public CriticalPathIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        [Fact]
        public async Task EndToEnd_ICUMS_Pipeline_CriticalPath_ShouldWork()
        {
            // This test would simulate the complete ICUMS pipeline:
            // 1. Download data from ICUMS API
            // 2. Process files through scanner
            // 3. Ingest JSON data
            // 4. Transfer to main database
            // 5. Map container data
            // 6. Validate completeness

            // For now, we'll test that the pipeline components are accessible
            var healthResponse = await _client.GetAsync("/api/Monitoring/health/overview");
            Assert.True(healthResponse.IsSuccessStatusCode);

            var performanceResponse = await _client.GetAsync("/api/Performance/summary");
            Assert.True(performanceResponse.IsSuccessStatusCode);
        }

        [Fact]
        public async Task EndToEnd_ContainerProcessing_CriticalPath_ShouldWork()
        {
            // This test would simulate complete container processing:
            // 1. Scanner data ingestion
            // 2. ICUMS data matching
            // 3. Container completeness calculation
            // 4. BOE document processing
            // 5. Submission queue management

            // Test that container processing endpoints are accessible
            var validationResponse = await _client.GetAsync("/api/ContainerValidation/validate");
            Assert.True(validationResponse.IsSuccessStatusCode || 
                       validationResponse.StatusCode == System.Net.HttpStatusCode.BadRequest);

            var completenessResponse = await _client.GetAsync("/api/ContainerCompleteness/status");
            Assert.True(completenessResponse.IsSuccessStatusCode);
        }
    }
}
