using NickScanCentralImagingPortal.Core.DTOs.Reports;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service interface for generating various system reports
    /// </summary>
    public interface IReportsService
    {
        /// <summary>
        /// Generate container summary report
        /// </summary>
        Task<ContainerSummaryReportDto> GetContainerSummaryReportAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Generate scanner performance report
        /// </summary>
        Task<ScannerPerformanceReportDto> GetScannerPerformanceReportAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Generate ICUMS activity report
        /// </summary>
        Task<ICUMSActivityReportDto> GetICUMSActivityReportAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Generate user activity report
        /// </summary>
        Task<UserActivityReportDto> GetUserActivityReportAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Generate vehicle imports report
        /// </summary>
        Task<VehicleImportsReportDto> GetVehicleImportsReportAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Generate validation summary report
        /// </summary>
        Task<ValidationSummaryReportDto> GetValidationSummaryReportAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Export report in specified format
        /// </summary>
        Task<ExportResultDto> ExportReportAsync(string reportType, string format, DateTime startDate, DateTime endDate);
    }
}

