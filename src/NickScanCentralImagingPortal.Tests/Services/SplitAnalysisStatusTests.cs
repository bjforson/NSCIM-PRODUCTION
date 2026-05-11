using NickScanCentralImagingPortal.Services.ImageSplitter;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Services
{
    public class SplitAnalysisStatusTests
    {
        [Theory]
        [InlineData("not_applicable", SplitAnalysisStatus.NotApplicable)]
        [InlineData("visual_single", SplitAnalysisStatus.VisualSingle)]
        [InlineData("uncertain", SplitAnalysisStatus.Uncertain)]
        [InlineData("completed_visual_single", SplitAnalysisStatus.VisualSingle)]
        public void ResolveForAnalysisRecord_MapsExplicitNonChoiceOutcome(string outcome, string expectedStatus)
        {
            var job = new SplitJobStatus(
                Guid.NewGuid(),
                outcome,
                BestStrategy: null,
                BestConfidence: null,
                SplitX: null,
                ResultCount: 0);

            var status = SplitAnalysisStatus.ResolveForAnalysisRecord(job);

            Assert.Equal(expectedStatus, status);
        }

        [Fact]
        public void ResolveForAnalysisRecord_CompletedWithNoResults_IsUncertain()
        {
            var job = new SplitJobStatus(
                Guid.NewGuid(),
                "completed",
                BestStrategy: null,
                BestConfidence: null,
                SplitX: null,
                ResultCount: 0);

            var status = SplitAnalysisStatus.ResolveForAnalysisRecord(job);

            Assert.Equal(SplitAnalysisStatus.Uncertain, status);
        }

        [Fact]
        public void ResolveForAnalysisRecord_CompletedWithExpectedResultsButFetchMiss_StaysPending()
        {
            var job = new SplitJobStatus(
                Guid.NewGuid(),
                "completed",
                BestStrategy: "steel_wall_midpoint",
                BestConfidence: 0.91,
                SplitX: 420,
                ResultCount: 2);

            var status = SplitAnalysisStatus.ResolveForAnalysisRecord(
                job,
                fetchedCandidateCount: 0,
                candidateFetchAttempted: true);

            Assert.Equal(SplitAnalysisStatus.Pending, status);
        }

        [Fact]
        public void ResolveForAnalysisRecord_CompletedWithCandidates_IsReady()
        {
            var job = new SplitJobStatus(
                Guid.NewGuid(),
                "completed",
                BestStrategy: "steel_wall_midpoint",
                BestConfidence: 0.91,
                SplitX: 420,
                ResultCount: 2);

            var status = SplitAnalysisStatus.ResolveForAnalysisRecord(
                job,
                fetchedCandidateCount: 2,
                candidateFetchAttempted: true);

            Assert.Equal(SplitAnalysisStatus.Ready, status);
        }

        [Fact]
        public void ShouldPreserveExistingResolution_PreservesChosenAndSameJobTerminal()
        {
            var jobId = Guid.NewGuid();

            Assert.True(SplitAnalysisStatus.ShouldPreserveExistingResolution(
                SplitAnalysisStatus.Chosen,
                existingJobId: null,
                incomingJobId: jobId));
            Assert.True(SplitAnalysisStatus.ShouldPreserveExistingResolution(
                SplitAnalysisStatus.VisualSingle,
                existingJobId: jobId,
                incomingJobId: jobId));
            Assert.False(SplitAnalysisStatus.ShouldPreserveExistingResolution(
                SplitAnalysisStatus.VisualSingle,
                existingJobId: Guid.NewGuid(),
                incomingJobId: jobId));
        }
    }
}
