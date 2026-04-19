using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IAiImageAssistService
    {
        Task<IReadOnlyList<AiImageAnalysisSuggestion>> GenerateSuggestionsForGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AiImageAnalysisSuggestion>> GenerateStubSuggestionsForGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
    }
}
