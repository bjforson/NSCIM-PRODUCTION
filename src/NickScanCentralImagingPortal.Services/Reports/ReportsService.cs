using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.Reports;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using QuestPDF.Fluent;

namespace NickScanCentralImagingPortal.Services.Reports
{
    /// <summary>
    /// Comprehensive service for generating various system reports
    /// </summary>
    public class ReportsService : IReportsService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ReportsService> _logger;

        public ReportsService(
            ApplicationDbContext context,
            ILogger<ReportsService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ContainerSummaryReportDto> GetContainerSummaryReportAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.LogInformation("Generating container summary report from {StartDate} to {EndDate}", startDate, endDate);

                var report = new ContainerSummaryReportDto
                {
                    GeneratedAt = DateTime.UtcNow,
                    StartDate = startDate,
                    EndDate = endDate
                };

                // Get basic container statistics
                var containersQuery = _context.Containers
                    .Where(c => c.CreatedAt >= startDate && c.CreatedAt <= endDate);

                report.TotalContainers = await containersQuery.CountAsync();

                // Use joins instead of navigation properties for better EF translation
                report.ScannedContainers = await containersQuery
                    .Join(_context.ContainerImages, c => c.Id, ci => ci.ContainerId, (c, ci) => c)
                    .Distinct()
                    .CountAsync();

                report.PendingContainers = report.TotalContainers - report.ScannedContainers;
                report.FailedContainers = await containersQuery.CountAsync(c => c.ProcessingStatus == "Failed");

                // Clearance type breakdown - using scanner type as proxy for now
                report.ImportContainers = await containersQuery.CountAsync(c => c.ScannerType.Contains("IM") || c.ScannerType.Contains("Import"));
                report.ExportContainers = await containersQuery.CountAsync(c => c.ScannerType.Contains("EX") || c.ScannerType.Contains("Export"));
                report.CMRContainers = await containersQuery.CountAsync(c => c.ScannerType.Contains("CMR"));

                // Daily statistics
                report.DailyStats = await containersQuery
                    .GroupBy(c => c.CreatedAt.Date)
                    .Select(g => new DailyContainerStatsDto
                    {
                        Date = g.Key,
                        TotalContainers = g.Count(),
                        ScannedContainers = g.Count(c => c.Images.Any()),
                        PendingContainers = g.Count(c => !c.Images.Any()),
                        FailedContainers = g.Count(c => c.ProcessingStatus == "Failed")
                    })
                    .OrderBy(s => s.Date)
                    .ToListAsync();

                // Scanner breakdown
                report.ScannerStats = await _context.ContainerImages
                    .Where(ci => ci.CreatedAt >= startDate && ci.CreatedAt <= endDate)
                    .GroupBy(ci => ci.Container.ScannerType)
                    .Select(g => new ScannerContainerStatsDto
                    {
                        ScannerName = g.Key,
                        TotalScans = g.Count(),
                        SuccessfulScans = g.Count(),
                        FailedScans = 0, // TODO: Implement failed scan tracking
                        SuccessRate = 100.0
                    })
                    .ToListAsync();

                // Top containers by images
                report.TopContainersByImages = await _context.Containers
                    .Where(c => c.CreatedAt >= startDate && c.CreatedAt <= endDate && c.Images.Any())
                    .Select(c => new TopContainerDto
                    {
                        ContainerNumber = c.ContainerId,
                        ImageCount = c.Images.Count,
                        ClearanceType = c.ScannerType, // Using scanner type as proxy
                        ScanDate = c.CreatedAt
                    })
                    .OrderByDescending(c => c.ImageCount)
                    .Take(10)
                    .ToListAsync();

                _logger.LogInformation("Generated container summary report with {TotalContainers} containers", report.TotalContainers);
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating container summary report");
                throw;
            }
        }

        public async Task<ScannerPerformanceReportDto> GetScannerPerformanceReportAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.LogInformation("Generating scanner performance report from {StartDate} to {EndDate}", startDate, endDate);

                var report = new ScannerPerformanceReportDto
                {
                    GeneratedAt = DateTime.UtcNow,
                    StartDate = startDate,
                    EndDate = endDate
                };

                // Get image processing statistics
                var imagesQuery = _context.ContainerImages
                    .Where(ci => ci.CreatedAt >= startDate && ci.CreatedAt <= endDate);

                report.TotalScans = await imagesQuery.CountAsync();
                report.SuccessfulScans = report.TotalScans; // Assume all scans are successful for now
                report.FailedScans = 0; // TODO: Implement failed scan tracking
                report.SuccessRate = report.TotalScans > 0 ? (double)report.SuccessfulScans / report.TotalScans * 100 : 0;

                // Scanner details
                report.ScannerDetails = await imagesQuery
                    .GroupBy(ci => ci.Container.ScannerType)
                    .Select(g => new ScannerDetailsDto
                    {
                        ScannerName = g.Key,
                        ScannerType = g.Key,
                        TotalScans = g.Count(),
                        SuccessfulScans = g.Count(),
                        FailedScans = 0,
                        SuccessRate = 100.0,
                        AverageProcessingTime = TimeSpan.FromSeconds(30) // TODO: Calculate actual processing time
                    })
                    .ToListAsync();

                // Daily performance
                report.DailyPerformance = await imagesQuery
                    .GroupBy(ci => ci.CreatedAt.Date)
                    .Select(g => new DailyPerformanceDto
                    {
                        Date = g.Key,
                        TotalScans = g.Count(),
                        SuccessfulScans = g.Count(),
                        FailedScans = 0,
                        AverageProcessingTime = TimeSpan.FromSeconds(30)
                    })
                    .OrderBy(p => p.Date)
                    .ToListAsync();

                // Performance metrics
                report.PerformanceMetrics = new List<PerformanceMetricDto>
                {
                    new() { MetricName = "Average Processing Time", Value = 30.0, Unit = "seconds" },
                    new() { MetricName = "Peak Throughput", Value = 100.0, Unit = "scans/hour" },
                    new() { MetricName = "System Uptime", Value = 99.5, Unit = "percent" }
                };

                _logger.LogInformation("Generated scanner performance report with {TotalScans} scans", report.TotalScans);
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating scanner performance report");
                throw;
            }
        }

        public async Task<ICUMSActivityReportDto> GetICUMSActivityReportAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.LogInformation("Generating ICUMS activity report from {StartDate} to {EndDate}", startDate, endDate);

                var report = new ICUMSActivityReportDto
                {
                    GeneratedAt = DateTime.UtcNow,
                    StartDate = startDate,
                    EndDate = endDate
                };

                // Get ICUMS download statistics
                var downloadsQuery = _context.ICUMSDownloadQueues
                    .Where(q => q.QueuedAt >= startDate && q.QueuedAt <= endDate);

                report.TotalDownloads = await downloadsQuery.CountAsync();
                report.SuccessfulDownloads = await downloadsQuery.CountAsync(q => q.Status == "Completed");
                report.FailedDownloads = await downloadsQuery.CountAsync(q => q.Status == "Failed");
                report.DownloadSuccessRate = report.TotalDownloads > 0 ? (double)report.SuccessfulDownloads / report.TotalDownloads * 100 : 0;

                // Submission statistics (mock data for now)
                report.TotalSubmissions = 0;
                report.SuccessfulSubmissions = 0;
                report.FailedSubmissions = 0;
                report.SubmissionSuccessRate = 0;

                // Queue statistics
                report.PendingDownloads = await _context.ICUMSDownloadQueues.CountAsync(q => q.Status == "Pending");
                report.PendingSubmissions = 0; // TODO: Implement submission queue

                // Daily activity
                report.DailyActivity = await downloadsQuery
                    .GroupBy(q => q.QueuedAt.Date)
                    .Select(g => new DailyICUMSActivityDto
                    {
                        Date = g.Key,
                        Downloads = g.Count(),
                        Submissions = 0, // TODO: Implement submission tracking
                        SuccessRate = 100.0
                    })
                    .OrderBy(a => a.Date)
                    .ToListAsync();

                // Error analysis
                report.ErrorAnalysis = await downloadsQuery
                    .Where(q => q.Status == "Failed")
                    .GroupBy(q => q.LastErrorMessage)
                    .Select(g => new ErrorAnalysisDto
                    {
                        ErrorType = g.Key ?? "Unknown Error",
                        Count = g.Count(),
                        Percentage = 0.0 // TODO: Calculate percentage
                    })
                    .ToListAsync();

                _logger.LogInformation("Generated ICUMS activity report with {TotalDownloads} downloads", report.TotalDownloads);
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating ICUMS activity report");
                throw;
            }
        }

        public Task<UserActivityReportDto> GetUserActivityReportAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.LogInformation("Generating user activity report from {StartDate} to {EndDate}", startDate, endDate);

                var report = new UserActivityReportDto
                {
                    GeneratedAt = DateTime.UtcNow,
                    StartDate = startDate,
                    EndDate = endDate
                };

                // TODO: Implement user activity tracking
                // This would require user activity logging tables
                report.TotalUsers = 0;
                report.ActiveUsers = 0;
                report.NewUsers = 0;
                report.UserActivityDetails = new List<UserActivityDetailsDto>();
                report.DailyActivity = new List<DailyUserActivityDto>();
                report.RoleActivity = new List<RoleActivityDto>();

                _logger.LogInformation("Generated user activity report");
                return Task.FromResult(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating user activity report");
                throw;
            }
        }

        public Task<VehicleImportsReportDto> GetVehicleImportsReportAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.LogInformation("Generating vehicle imports report from {StartDate} to {EndDate}", startDate, endDate);

                var report = new VehicleImportsReportDto
                {
                    GeneratedAt = DateTime.UtcNow,
                    StartDate = startDate,
                    EndDate = endDate
                };

                // TODO: Implement vehicle imports tracking
                // This would require vehicle-specific data tables
                report.TotalVehicles = 0;
                report.ProcessedVehicles = 0;
                report.PendingVehicles = 0;
                report.FailedVehicles = 0;
                report.DailyStats = new List<DailyVehicleStatsDto>();
                report.VehicleTypeStats = new List<VehicleTypeStatsDto>();
                report.ProcessingStatusStats = new List<VehicleProcessingStatusDto>();

                _logger.LogInformation("Generated vehicle imports report");
                return Task.FromResult(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating vehicle imports report");
                throw;
            }
        }

        public async Task<ValidationSummaryReportDto> GetValidationSummaryReportAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.LogInformation("Generating validation summary report from {StartDate} to {EndDate}", startDate, endDate);

                var report = new ValidationSummaryReportDto
                {
                    GeneratedAt = DateTime.UtcNow,
                    StartDate = startDate,
                    EndDate = endDate
                };

                // Get validation statistics
                var containersQuery = _context.Containers
                    .Where(c => c.CreatedAt >= startDate && c.CreatedAt <= endDate);

                report.TotalValidations = await containersQuery.CountAsync();
                report.PassedValidations = await containersQuery.CountAsync(c => c.Images.Any());
                report.FailedValidations = await containersQuery.CountAsync(c => !c.Images.Any());
                report.ValidationPassRate = report.TotalValidations > 0 ? (double)report.PassedValidations / report.TotalValidations * 100 : 0;

                // Validation types (mock data)
                report.ValidationTypes = new List<ValidationTypeStatsDto>
                {
                    new() { ValidationType = "Image Completeness", TotalValidations = report.TotalValidations, PassedValidations = report.PassedValidations, FailedValidations = report.FailedValidations, PassRate = report.ValidationPassRate },
                    new() { ValidationType = "ICUMS Data", TotalValidations = report.TotalValidations, PassedValidations = report.PassedValidations, FailedValidations = report.FailedValidations, PassRate = report.ValidationPassRate }
                };

                // Common issues (mock data)
                report.CommonIssues = new List<ValidationIssueDto>
                {
                    new() { IssueType = "Missing Images", Count = 25, Description = "Container has no scanned images" },
                    new() { IssueType = "Missing ICUMS Data", Count = 15, Description = "Container has no ICUMS data" },
                    new() { IssueType = "Incomplete Data", Count = 8, Description = "Container data is incomplete" }
                };

                // Daily validation stats
                report.DailyStats = await containersQuery
                    .GroupBy(c => c.CreatedAt.Date)
                    .Select(g => new DailyValidationStatsDto
                    {
                        Date = g.Key,
                        TotalValidations = g.Count(),
                        PassedValidations = g.Count(c => c.Images.Any()),
                        FailedValidations = g.Count(c => !c.Images.Any()),
                        PassRate = g.Count() > 0 ? (double)g.Count(c => c.Images.Any()) / g.Count() * 100 : 0
                    })
                    .OrderBy(s => s.Date)
                    .ToListAsync();

                _logger.LogInformation("Generated validation summary report with {TotalValidations} validations", report.TotalValidations);
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating validation summary report");
                throw;
            }
        }

        public async Task<ExportResultDto> ExportReportAsync(string reportType, string format, DateTime startDate, DateTime endDate)
        {
            try
            {
                _logger.LogInformation("Exporting {ReportType} report in {Format} format", reportType, format);

                // Generate the report data based on type
                object reportData = reportType.ToLower() switch
                {
                    "container" => await GetContainerSummaryReportAsync(startDate, endDate),
                    "scanner" => await GetScannerPerformanceReportAsync(startDate, endDate),
                    "icums" => await GetICUMSActivityReportAsync(startDate, endDate),
                    "user" => await GetUserActivityReportAsync(startDate, endDate),
                    "vehicle" => await GetVehicleImportsReportAsync(startDate, endDate),
                    "validation" => await GetValidationSummaryReportAsync(startDate, endDate),
                    _ => throw new ArgumentException($"Unknown report type: {reportType}")
                };

                // Convert to byte array based on format
                byte[] data = format.ToLower() switch
                {
                    "excel" => await ExportToExcelAsync(reportData, reportType),
                    "pdf" => await ExportToPdfAsync(reportData, reportType),
                    "csv" => await ExportToCsvAsync(reportData, reportType),
                    _ => throw new ArgumentException($"Unknown format: {format}")
                };

                var fileName = $"{reportType}_{format}_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.{GetFileExtension(format)}";

                return new ExportResultDto
                {
                    Success = true,
                    FileName = fileName,
                    ContentType = GetContentType(format),
                    Data = data
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting report");
                return new ExportResultDto
                {
                    Success = false,
                    FileName = "",
                    ContentType = "application/octet-stream",
                    Data = new byte[0],
                    ErrorMessage = ex.Message
                };
            }
        }

        private Task<byte[]> ExportToExcelAsync(object reportData, string reportType)
        {
            var (headers, rows) = FlattenReportData(reportData, reportType);

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add(reportType);

            // Title row
            var titleRange = ws.Range(1, 1, 1, Math.Max(headers.Length, 1));
            titleRange.Merge();
            titleRange.Value = $"NSCIS {reportType} Report";
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.FontSize = 14;

            // Headers
            int headerRow = 3;
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(headerRow, c + 1).Value = headers[c];
            var headerRange = ws.Range(headerRow, 1, headerRow, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1a237e");
            headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

            // Data rows
            int row = headerRow + 1;
            foreach (var values in rows)
            {
                for (int c = 0; c < values.Length && c < headers.Length; c++)
                    ws.Cell(row, c + 1).Value = values[c];
                row++;
            }

            ws.SheetView.FreezeRows(headerRow);
            if (row - headerRow < 10000) ws.Columns().AdjustToContents();
            if (row > headerRow + 1) ws.Range(headerRow, 1, row - 1, headers.Length).SetAutoFilter();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return Task.FromResult(stream.ToArray());
        }

        private Task<byte[]> ExportToPdfAsync(object reportData, string reportType)
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            var (headers, rows) = FlattenReportData(reportData, reportType);
            var generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var doc = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(new QuestPDF.Helpers.PageSize(297, 210, QuestPDF.Infrastructure.Unit.Millimetre));
                    page.Margin(1.5f, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Column(col =>
                    {
                        col.Item().Background("#1a237e").Padding(8).Row(r =>
                        {
                            r.RelativeItem().Column(inner =>
                            {
                                inner.Item().Text("NICKSCAN CENTRAL IMAGING SYSTEM")
                                    .FontColor(QuestPDF.Helpers.Colors.White).FontSize(10).Bold();
                                inner.Item().Text($"{reportType} Report")
                                    .FontColor(QuestPDF.Helpers.Colors.White).FontSize(14).Bold();
                            });
                            r.ConstantItem(200).AlignRight().Text($"Generated: {generatedAt}")
                                .FontColor("#e3e3e3").FontSize(8);
                        });
                    });

                    page.Content().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (var _ in headers) columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            foreach (var h in headers)
                                header.Cell().Background("#e8eaf6").Border(0.5f)
                                    .BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten1)
                                    .Padding(4).Text(h).Bold().FontSize(9);
                        });

                        for (int i = 0; i < rows.Count; i++)
                        {
                            var stripe = i % 2 == 0 ? QuestPDF.Helpers.Colors.White : QuestPDF.Helpers.Colors.Grey.Lighten4;
                            foreach (var cell in rows[i])
                                table.Cell().Background(stripe).Border(0.25f)
                                    .BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2)
                                    .Padding(3).Text(cell ?? "").FontSize(8);
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.CurrentPageNumber().FontSize(8);
                        x.Span(" / ").FontSize(8);
                        x.TotalPages().FontSize(8);
                    });
                });
            });

            return Task.FromResult(doc.GeneratePdf());
        }

        private Task<byte[]> ExportToCsvAsync(object reportData, string reportType)
        {
            var (headers, rows) = FlattenReportData(reportData, reportType);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
            foreach (var values in rows)
                sb.AppendLine(string.Join(",", values.Select(EscapeCsv)));

            var bodyBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var result = new byte[bom.Length + bodyBytes.Length];
            Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
            Buffer.BlockCopy(bodyBytes, 0, result, bom.Length, bodyBytes.Length);
            return Task.FromResult(result);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        /// <summary>
        /// Flattens a report DTO into tabular headers + rows for export.
        /// Each report type maps its summary + detail rows into a uniform table.
        /// </summary>
        private (string[] Headers, List<string[]> Rows) FlattenReportData(object reportData, string reportType)
        {
            return reportType.ToLower() switch
            {
                "container" => FlattenContainerReport((ContainerSummaryReportDto)reportData),
                "scanner" => FlattenScannerReport((ScannerPerformanceReportDto)reportData),
                "icums" => FlattenICUMSReport((ICUMSActivityReportDto)reportData),
                "validation" => FlattenValidationReport((ValidationSummaryReportDto)reportData),
                "user" => FlattenUserReport((UserActivityReportDto)reportData),
                "vehicle" => FlattenVehicleReport((VehicleImportsReportDto)reportData),
                _ => (new[] { "Data" }, new List<string[]> { new[] { System.Text.Json.JsonSerializer.Serialize(reportData) } })
            };
        }

        private (string[], List<string[]>) FlattenContainerReport(ContainerSummaryReportDto r)
        {
            var headers = new[] { "Date", "Total", "Scanned", "Pending", "Failed" };
            var rows = r.DailyStats.Select(d => new[]
            {
                d.Date.ToString("yyyy-MM-dd"), d.TotalContainers.ToString(),
                d.ScannedContainers.ToString(), d.PendingContainers.ToString(), d.FailedContainers.ToString()
            }).ToList();

            // Prepend summary row
            rows.Insert(0, new[] { $"{r.StartDate:yyyy-MM-dd} to {r.EndDate:yyyy-MM-dd} (Summary)",
                r.TotalContainers.ToString(), r.ScannedContainers.ToString(),
                r.PendingContainers.ToString(), r.FailedContainers.ToString() });
            return (headers, rows);
        }

        private (string[], List<string[]>) FlattenScannerReport(ScannerPerformanceReportDto r)
        {
            var headers = new[] { "Scanner", "Total Scans", "Successful", "Failed", "Success Rate %" };
            var rows = r.ScannerDetails.Select(d => new[]
            {
                d.ScannerName, d.TotalScans.ToString(), d.SuccessfulScans.ToString(),
                d.FailedScans.ToString(), d.SuccessRate.ToString("F1")
            }).ToList();

            rows.Insert(0, new[] { "ALL SCANNERS (Summary)", r.TotalScans.ToString(),
                r.SuccessfulScans.ToString(), r.FailedScans.ToString(), r.SuccessRate.ToString("F1") });
            return (headers, rows);
        }

        private (string[], List<string[]>) FlattenICUMSReport(ICUMSActivityReportDto r)
        {
            var headers = new[] { "Date", "Downloads", "Download Failures", "Submissions", "Submission Failures" };
            var rows = r.DailyActivity.Select(d => new[]
            {
                d.Date.ToString("yyyy-MM-dd"), d.Downloads.ToString(), d.FailedDownloads.ToString(),
                d.Submissions.ToString(), d.FailedSubmissions.ToString()
            }).ToList();

            rows.Insert(0, new[] { $"{r.StartDate:yyyy-MM-dd} to {r.EndDate:yyyy-MM-dd} (Summary)",
                r.TotalDownloads.ToString(), r.FailedDownloads.ToString(),
                r.TotalSubmissions.ToString(), r.FailedSubmissions.ToString() });
            return (headers, rows);
        }

        private (string[], List<string[]>) FlattenValidationReport(ValidationSummaryReportDto r)
        {
            var headers = new[] { "Date", "Total", "Passed", "Failed", "Pass Rate %" };
            var rows = r.DailyStats.Select(d => new[]
            {
                d.Date.ToString("yyyy-MM-dd"), d.TotalValidations.ToString(),
                d.PassedValidations.ToString(), d.FailedValidations.ToString(),
                d.TotalValidations > 0 ? (d.PassedValidations * 100.0 / d.TotalValidations).ToString("F1") : "0"
            }).ToList();

            rows.Insert(0, new[] { $"{r.StartDate:yyyy-MM-dd} to {r.EndDate:yyyy-MM-dd} (Summary)",
                r.TotalValidations.ToString(), r.PassedValidations.ToString(),
                r.FailedValidations.ToString(), r.ValidationPassRate.ToString("F1") });
            return (headers, rows);
        }

        private (string[], List<string[]>) FlattenUserReport(UserActivityReportDto r)
        {
            var headers = new[] { "Username", "Role", "Actions", "Logins", "Last Login" };
            var rows = r.UserActivityDetails.Select(d => new[]
            {
                d.Username, d.Role, d.ActionsPerformed.ToString(), d.LoginCount.ToString(),
                d.LastLogin.ToString("yyyy-MM-dd HH:mm")
            }).ToList();
            return (headers, rows);
        }

        private (string[], List<string[]>) FlattenVehicleReport(VehicleImportsReportDto r)
        {
            var headers = new[] { "Date", "Total Vehicles", "VINs Extracted", "VINs Failed", "Extraction Rate %" };
            var rows = r.DailyStats.Select(d => new[]
            {
                d.Date.ToString("yyyy-MM-dd"), d.TotalVehicles.ToString(),
                d.VINsExtracted.ToString(), d.VINsFailed.ToString(), d.ExtractionRate.ToString("F1")
            }).ToList();
            return (headers, rows);
        }

        private static string GetFileExtension(string format)
        {
            return format.ToLower() switch
            {
                "excel" => "xlsx",
                "pdf" => "pdf",
                "csv" => "csv",
                _ => "txt"
            };
        }

        private static string GetContentType(string format)
        {
            return format.ToLower() switch
            {
                "excel" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "pdf" => "application/pdf",
                "csv" => "text/csv",
                _ => "application/octet-stream"
            };
        }
    }
}
