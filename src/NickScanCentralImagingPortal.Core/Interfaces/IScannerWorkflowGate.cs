namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IScannerWorkflowGate
    {
        bool IsAssignmentIntakeEnabled(string? scannerType);

        bool IsSplitIntakeEnabled(string? scannerType);
    }
}
