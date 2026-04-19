namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for implementing approved fixes by creating Git branches
    /// </summary>
    public interface IFixImplementationService
    {
        /// <summary>
        /// Implement an approved fix by creating a Git branch and applying changes
        /// </summary>
        Task<ImplementationResult> ImplementFixAsync(long proposalId, CancellationToken cancellationToken);
    }

    public class ImplementationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? BranchName { get; set; }
        public string? CommitHash { get; set; }
    }
}

