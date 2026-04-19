using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Links human final decisions to prior AI suggestions for training lineage.
    /// </summary>
    public interface IAiWorkflowLineageService
    {
        Task NotifyHumanDecisionAsync(
            string containerNumber,
            string scannerType,
            string? normalizedDecision,
            string? normalizedGroupIdentifierForStorage,
            string reviewedBy,
            CancellationToken cancellationToken = default);
    }
}
