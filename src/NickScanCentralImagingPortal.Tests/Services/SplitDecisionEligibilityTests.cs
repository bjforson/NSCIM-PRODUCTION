using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
using NickScanCentralImagingPortal.Services.ImageSplitter;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    public class SplitDecisionEligibilityTests
    {
        [Fact]
        public void ReadyMultiContainerRecord_RequiresSplitResolutionAndRejectsDecisions()
        {
            var record = CreateRecord(SplitAnalysisStatus.Ready, Guid.NewGuid(), splitResultId: null);
            var decision = CreateDecision(record.SplitJobId, splitResultId: null);

            Assert.True(SplitDecisionEligibility.RequiresSplitResolution(record));
            Assert.False(SplitDecisionEligibility.IsResolvedForDecision(record));
            Assert.False(SplitDecisionEligibility.IsDecisionCompatible(record, decision));
        }

        [Fact]
        public void ChosenMultiContainerRecord_RequiresMatchingJobAndResult()
        {
            var jobId = Guid.NewGuid();
            var resultId = Guid.NewGuid();
            var record = CreateRecord(SplitAnalysisStatus.Chosen, jobId, resultId);

            Assert.False(SplitDecisionEligibility.RequiresSplitResolution(record));
            Assert.True(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(jobId, resultId)));
            Assert.False(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(jobId, Guid.NewGuid())));
            Assert.False(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(Guid.NewGuid(), resultId)));
            Assert.False(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(jobId, splitResultId: null)));
        }

        [Theory]
        [InlineData(SplitAnalysisStatus.Skipped)]
        [InlineData(SplitAnalysisStatus.NotApplicable)]
        [InlineData(SplitAnalysisStatus.VisualSingle)]
        [InlineData(SplitAnalysisStatus.Uncertain)]
        public void TerminalNonChoiceRecord_AllowsDecisionWithoutSplitResultOnly(string splitStatus)
        {
            var jobId = Guid.NewGuid();
            var record = CreateRecord(splitStatus, jobId, splitResultId: null);

            Assert.False(SplitDecisionEligibility.RequiresSplitResolution(record));
            Assert.True(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(jobId, splitResultId: null)));
            Assert.True(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(splitJobId: null, splitResultId: null)));
            Assert.False(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(jobId, Guid.NewGuid())));
            Assert.False(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(Guid.NewGuid(), splitResultId: null)));
        }

        [Fact]
        public void NonSplitRecord_DoesNotRequireSplitLineage()
        {
            var record = CreateRecord(splitStatus: null, splitJobId: null, splitResultId: null, isMultiContainer: false);

            Assert.False(SplitDecisionEligibility.RequiresSplitResolution(record));
            Assert.True(SplitDecisionEligibility.IsResolvedForDecision(record));
            Assert.True(SplitDecisionEligibility.IsDecisionCompatible(record, CreateDecision(splitJobId: null, splitResultId: null)));
        }

        private static AnalysisRecord CreateRecord(
            string? splitStatus,
            Guid? splitJobId,
            Guid? splitResultId,
            bool isMultiContainer = true)
        {
            return new AnalysisRecord
            {
                ContainerNumber = "TCLU1234567",
                ScannerType = "ASE",
                IsMultiContainerScan = isMultiContainer,
                SplitStatus = splitStatus,
                SplitJobId = splitJobId,
                SplitResultId = splitResultId
            };
        }

        private static ImageAnalysisDecision CreateDecision(Guid? splitJobId, Guid? splitResultId)
        {
            return new ImageAnalysisDecision
            {
                ContainerNumber = "TCLU1234567",
                ScannerType = "ASE",
                Decision = "Normal",
                ReviewedBy = "test",
                SplitJobId = splitJobId,
                SplitResultId = splitResultId
            };
        }
    }
}
