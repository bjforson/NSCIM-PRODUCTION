-- Check AnalysisSettings (IntakeWorker/Orchestrator checks settings.Enabled)
SELECT Id, [Enabled], AssignmentMode, MaxConcurrentPerUser, LeaseMinutes, CreatedAtUtc, UpdatedAtUtc
FROM AnalysisSettings;
