using NickScanCentralImagingPortal.Core.Constants;
using CorePermissions = NickScanCentralImagingPortal.Core.Constants.Permissions;

namespace NickScanWebApp.New.Services.Permissions
{
    /// <summary>
    /// Strongly-typed permission identifiers sourced from backend constants.
    /// </summary>
    public static class PermissionIds
    {
        public static class Pages
        {
            public static PermissionId DashboardView { get; } = new(CorePermissions.PagesDashboardView);
            public static PermissionId DashboardAnalytics { get; } = new(CorePermissions.PagesDashboardAnalytics);
            public static PermissionId ContainersView { get; } = new(CorePermissions.PagesContainersView);
            public static PermissionId ContainersDetails { get; } = new(CorePermissions.PagesContainersDetails);
            public static PermissionId ContainerProcessing { get; } = new(CorePermissions.PagesContainerProcessing);
            public static PermissionId ContainerCompleteness { get; } = new(CorePermissions.PagesContainerCompleteness);
            public static PermissionId ImageAnalysisView { get; } = new(CorePermissions.PagesImageAnalysisView);
            public static PermissionId ImageAnalysisAudit { get; } = new(CorePermissions.PagesImageAnalysisAudit);
            public static PermissionId ImageAnalysisManagement { get; } = new(CorePermissions.PagesImageAnalysisManagement);
            public static PermissionId ValidationRules { get; } = new(CorePermissions.PagesValidationRules);
            public static PermissionId ValidationCompleteness { get; } = new(CorePermissions.PagesValidationCompleteness);
            public static PermissionId ValidationMatchCorrections { get; } = new(CorePermissions.PagesValidationMatchCorrections);
            public static PermissionId ValidationBoeLookup { get; } = new(CorePermissions.PagesValidationBoeLookup);
            public static PermissionId ValidationRecordCompleteness { get; } = new(CorePermissions.PagesValidationRecordCompleteness);
            public static PermissionId ValidationXrayInspector { get; } = new(CorePermissions.PagesValidationXrayInspector);
            public static PermissionId CmrValidation { get; } = new(CorePermissions.PagesCmrValidation);
            public static PermissionId CompletedRecords { get; } = new(CorePermissions.PagesCompletedRecords);
            public static PermissionId CrossRecordScans { get; } = new(CorePermissions.PagesCrossRecordScans);
            public static PermissionId VehiclesView { get; } = new(CorePermissions.PagesVehiclesView);
            public static PermissionId ScannersView { get; } = new(CorePermissions.PagesScannersView);
            public static PermissionId ScannersAse { get; } = new(CorePermissions.PagesScannersAse);
            public static PermissionId ScannersFs6000 { get; } = new(CorePermissions.PagesScannersFs6000);
            public static PermissionId ScannersHeimann { get; } = new(CorePermissions.PagesScannersHeimann);
            public static PermissionId IcumsView { get; } = new(CorePermissions.PagesIcumsView);
            public static PermissionId IcumsDownloadQueue { get; } = new(CorePermissions.PagesIcumsDownloadQueue);
            public static PermissionId IcumsSubmissionQueue { get; } = new(CorePermissions.PagesIcumsSubmissionQueue);
            public static PermissionId IcumsBoeRequest { get; } = new(CorePermissions.PagesIcumsBoeRequest);
            public static PermissionId IcumsLooseCargo { get; } = new(CorePermissions.PagesIcumsLooseCargo);
            public static PermissionId IcumsAnalytics { get; } = new(CorePermissions.PagesIcumsAnalytics);
            public static PermissionId IcumsBatchDownload { get; } = new(CorePermissions.PagesIcumsBatchDownload);
            public static PermissionId IcumsPayloads { get; } = new(CorePermissions.PagesIcumsPayloads);
            public static PermissionId IcumsVerifyStatus { get; } = new(CorePermissions.PagesIcumsVerifyStatus);
            public static PermissionId AdminUsers { get; } = new(CorePermissions.PagesAdminUsers);
            public static PermissionId AdminRoles { get; } = new(CorePermissions.PagesAdminRoles);
            public static PermissionId AdminPermissions { get; } = new(CorePermissions.PagesAdminPermissions);
            public static PermissionId AdminSettings { get; } = new(CorePermissions.PagesAdminSettings);
            public static PermissionId AdminLogs { get; } = new(CorePermissions.PagesAdminLogs);
            public static PermissionId AdminDatabase { get; } = new(CorePermissions.PagesAdminDatabase);
            public static PermissionId AdminAudit { get; } = new(CorePermissions.PagesAdminAudit);
            public static PermissionId AdminServiceControl { get; } = new(CorePermissions.PagesAdminServiceControl);
            public static PermissionId ReportsView { get; } = new(CorePermissions.PagesReportsView);
            public static PermissionId ReportsTemplates { get; } = new(CorePermissions.PagesReportsTemplates);
            public static PermissionId Search { get; } = new(CorePermissions.PagesSearch);
            public static PermissionId Notifications { get; } = new(CorePermissions.PagesNotifications);
            public static PermissionId Performance { get; } = new(CorePermissions.PagesPerformance);
            public static PermissionId ServicesMonitoring { get; } = new(CorePermissions.PagesServicesMonitoring);
            public static PermissionId ServicesPerformanceMetrics { get; } = new(CorePermissions.PagesServicesPerformanceMetrics);
            public static PermissionId ServicesDatabase { get; } = new(CorePermissions.PagesServicesDatabase);
            public static PermissionId ServicesDiagnostics { get; } = new(CorePermissions.PagesServicesDiagnostics);
            public static PermissionId ServicesGateway { get; } = new(CorePermissions.PagesServicesGateway);
            public static PermissionId ServicesIngestion { get; } = new(CorePermissions.PagesServicesIngestion);
            public static PermissionId ServicesImageProcessing { get; } = new(CorePermissions.PagesServicesImageProcessing);
            public static PermissionId ServicesAseSync { get; } = new(CorePermissions.PagesServicesAseSync);
            public static PermissionId ServicesFs6000Completeness { get; } = new(CorePermissions.PagesServicesFs6000Completeness);
            public static PermissionId ServicesConsolidatedCargo { get; } = new(CorePermissions.PagesServicesConsolidatedCargo);
            public static PermissionId ServicesAccessReview { get; } = new(CorePermissions.PagesServicesAccessReview);
            public static PermissionId ServicesDebug { get; } = new(CorePermissions.PagesServicesDebug);
            public static PermissionId OperationsErrors { get; } = new(CorePermissions.PagesOperationsErrors);
        }

        public static class Images
        {
            public static PermissionId View { get; } = new(CorePermissions.ImagesView);
            public static PermissionId Annotate { get; } = new(CorePermissions.ImagesAnnotate);
            public static PermissionId Edit { get; } = new(CorePermissions.ImagesEdit);
        }

        public static class Containers
        {
            public static PermissionId Approve { get; } = new(CorePermissions.ContainersApprove);
            public static PermissionId Reject { get; } = new(CorePermissions.ContainersReject);
            public static PermissionId Validate { get; } = new(CorePermissions.ContainersValidate);
            public static PermissionId Export { get; } = new(CorePermissions.ContainersExport);
        }

        #region Legacy Shortcuts
        public static PermissionId PagesDashboardView => Pages.DashboardView;
        public static PermissionId PagesDashboardAnalytics => Pages.DashboardAnalytics;
        public static PermissionId PagesContainersView => Pages.ContainersView;
        public static PermissionId PagesContainersDetails => Pages.ContainersDetails;
        public static PermissionId PagesContainerProcessing => Pages.ContainerProcessing;
        public static PermissionId PagesContainerCompleteness => Pages.ContainerCompleteness;
        public static PermissionId PagesImageAnalysisView => Pages.ImageAnalysisView;
        public static PermissionId PagesImageAnalysisAudit => Pages.ImageAnalysisAudit;
        public static PermissionId PagesImageAnalysisManagement => Pages.ImageAnalysisManagement;
        public static PermissionId PagesValidationRules => Pages.ValidationRules;
        public static PermissionId PagesValidationCompleteness => Pages.ValidationCompleteness;
        public static PermissionId PagesCmrValidation => Pages.CmrValidation;
        public static PermissionId PagesCompletedRecords => Pages.CompletedRecords;
        public static PermissionId PagesCrossRecordScans => Pages.CrossRecordScans;
        public static PermissionId PagesVehiclesView => Pages.VehiclesView;
        public static PermissionId PagesScannersView => Pages.ScannersView;
        public static PermissionId PagesScannersAse => Pages.ScannersAse;
        public static PermissionId PagesScannersFs6000 => Pages.ScannersFs6000;
        public static PermissionId PagesScannersHeimann => Pages.ScannersHeimann;
        public static PermissionId PagesIcumsView => Pages.IcumsView;
        public static PermissionId PagesIcumsDownloadQueue => Pages.IcumsDownloadQueue;
        public static PermissionId PagesIcumsSubmissionQueue => Pages.IcumsSubmissionQueue;
        public static PermissionId PagesIcumsBoeRequest => Pages.IcumsBoeRequest;
        public static PermissionId PagesIcumsLooseCargo => Pages.IcumsLooseCargo;
        public static PermissionId PagesIcumsAnalytics => Pages.IcumsAnalytics;
        public static PermissionId PagesIcumsBatchDownload => Pages.IcumsBatchDownload;
        public static PermissionId PagesIcumsPayloads => Pages.IcumsPayloads;
        public static PermissionId PagesIcumsVerifyStatus => Pages.IcumsVerifyStatus;
        public static PermissionId PagesAdminUsers => Pages.AdminUsers;
        public static PermissionId PagesAdminRoles => Pages.AdminRoles;
        public static PermissionId PagesAdminPermissions => Pages.AdminPermissions;
        public static PermissionId PagesAdminSettings => Pages.AdminSettings;
        public static PermissionId PagesAdminLogs => Pages.AdminLogs;
        public static PermissionId PagesAdminDatabase => Pages.AdminDatabase;
        public static PermissionId PagesAdminAudit => Pages.AdminAudit;
        public static PermissionId PagesAdminServiceControl => Pages.AdminServiceControl;
        public static PermissionId PagesReportsView => Pages.ReportsView;
        public static PermissionId PagesReportsTemplates => Pages.ReportsTemplates;
        public static PermissionId PagesSearch => Pages.Search;
        public static PermissionId PagesNotifications => Pages.Notifications;
        public static PermissionId PagesPerformance => Pages.Performance;
        public static PermissionId PagesServicesMonitoring => Pages.ServicesMonitoring;
        public static PermissionId PagesServicesPerformanceMetrics => Pages.ServicesPerformanceMetrics;
        public static PermissionId PagesServicesDatabase => Pages.ServicesDatabase;
        public static PermissionId PagesServicesDiagnostics => Pages.ServicesDiagnostics;
        public static PermissionId PagesServicesGateway => Pages.ServicesGateway;
        public static PermissionId PagesServicesIngestion => Pages.ServicesIngestion;
        public static PermissionId PagesServicesImageProcessing => Pages.ServicesImageProcessing;
        public static PermissionId PagesServicesAseSync => Pages.ServicesAseSync;
        public static PermissionId PagesServicesFs6000Completeness => Pages.ServicesFs6000Completeness;
        public static PermissionId PagesServicesConsolidatedCargo => Pages.ServicesConsolidatedCargo;
        public static PermissionId PagesServicesAccessReview => Pages.ServicesAccessReview;
        public static PermissionId PagesServicesDebug => Pages.ServicesDebug;
        public static PermissionId PagesOperationsErrors => Pages.OperationsErrors;

        public static PermissionId ImagesView => Images.View;
        public static PermissionId ImagesAnnotate => Images.Annotate;
        public static PermissionId ImagesEdit => Images.Edit;

        public static PermissionId ContainersApprove => Containers.Approve;
        public static PermissionId ContainersReject => Containers.Reject;
        public static PermissionId ContainersValidate => Containers.Validate;
        public static PermissionId ContainersExport => Containers.Export;
        #endregion
    }
}

