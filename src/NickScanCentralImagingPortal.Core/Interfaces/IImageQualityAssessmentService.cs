using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IImageQualityAssessmentService
    {
        Task<QualityAssessment> AssessQualityAsync(string containerNumber);
    }
}
