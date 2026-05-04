using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service interface for mapping scanner data to ICUMS BOE data
    /// </summary>
    public interface IContainerDataMapperService
    {
        /// <summary>
        /// Maps scanner data to ICUMS BOE data for a specific container
        /// </summary>
        /// <param name="containerNumber">Container number to map</param>
        /// <param name="scannerType">Type of scanner (FS6000, ASE, etc.)</param>
        /// <param name="scannerDataId">ID of the scanner data record</param>
        /// <param name="icumsDataId">ID of the ICUMS BOE data record</param>
        /// <returns>Created relation record, or null when the cardinal port rule
        /// blocks the mapping (e.g. FS6000 scan against a TMA-port BOE).</returns>
        Task<ContainerBOERelation?> MapContainerDataAsync(string containerNumber, string scannerType, int scannerDataId, int icumsDataId);

        /// <summary>
        /// Gets all mapped relations for a container
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <returns>List of mapped relations</returns>
        Task<List<ContainerBOERelation>> GetContainerMappingsAsync(string containerNumber);

        /// <summary>
        /// Validates that a mapping is correct and complete
        /// </summary>
        /// <param name="relationId">Relation ID to validate</param>
        /// <returns>Validation result</returns>
        Task<MappingValidationResult> ValidateMappingAsync(int relationId);

        /// <summary>
        /// Gets containers ready for ICUMS submission
        /// </summary>
        /// <param name="limit">Maximum number of containers to return (default: 100)</param>
        /// <returns>List of containers with complete mappings</returns>
        Task<List<ContainerSubmissionData>> GetContainersReadyForSubmissionAsync(int limit = 100);

        /// <summary>
        /// Processes all pending mappings
        /// </summary>
        /// <param name="stoppingToken">Cancellation token</param>
        Task ProcessPendingMappingsAsync(CancellationToken stoppingToken);
    }
}
