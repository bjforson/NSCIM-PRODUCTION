-- Seed SystemSettings with all configurable values
-- Each setting preserves the current hardcoded default as the value
-- Categories: Database, RateLimiting, Authentication, ImageAnalysis, Validation, ICUMS, Caching, UI, Alerts, Capacity, HealthChecks

INSERT INTO systemsettings 
  (category, settingkey, settingvalue, datatype, description, defaultvalue, isencrypted, requiresrestart, allowedroles, isactive, displayorder, validationrules, lastmodifiedby, lastmodifiedat, createdat, updatedat)
VALUES
-- ==================== Database ====================
('Database', 'CommandTimeoutSeconds', '120', 'int', 'SQL command timeout in seconds for all DB contexts', '120', false, true, NULL, true, 1, '{"min":10,"max":600}', 'System', NOW(), NOW(), NOW()),
('Database', 'MaxRetryCount', '3', 'int', 'Max number of retries on transient DB failures', '3', false, true, NULL, true, 2, '{"min":0,"max":10}', 'System', NOW(), NOW(), NOW()),
('Database', 'MaxRetryDelaySeconds', '5', 'int', 'Delay between DB retry attempts in seconds', '5', false, true, NULL, true, 3, '{"min":1,"max":60}', 'System', NOW(), NOW(), NOW()),

-- ==================== RateLimiting ====================
('RateLimiting', 'Login.PermitLimit', '5', 'int', 'Max login attempts per minute', '5', false, false, NULL, true, 1, '{"min":1,"max":100}', 'System', NOW(), NOW(), NOW()),
('RateLimiting', 'API.PermitLimit', '500', 'int', 'Max API requests per minute', '500', false, false, NULL, true, 2, '{"min":10,"max":10000}', 'System', NOW(), NOW(), NOW()),
('RateLimiting', 'Dashboard.PermitLimit', '200', 'int', 'Max dashboard requests per minute', '200', false, false, NULL, true, 3, '{"min":10,"max":5000}', 'System', NOW(), NOW(), NOW()),
('RateLimiting', 'Export.PermitLimit', '50', 'int', 'Max export requests per minute', '50', false, false, NULL, true, 4, '{"min":1,"max":500}', 'System', NOW(), NOW(), NOW()),
('RateLimiting', 'Admin.PermitLimit', '1000', 'int', 'Max admin API requests per minute', '1000', false, false, NULL, true, 5, '{"min":10,"max":10000}', 'System', NOW(), NOW(), NOW()),

-- ==================== Authentication ====================
('Authentication', 'Jwt.ExpiresInSeconds', '28800', 'int', 'JWT token expiration time (28800 = 8 hours)', '28800', false, true, NULL, true, 1, '{"min":300,"max":86400}', 'System', NOW(), NOW(), NOW()),
('Authentication', 'Jwt.ClockSkewMinutes', '1', 'int', 'JWT clock skew tolerance in minutes', '1', false, true, NULL, true, 2, '{"min":0,"max":10}', 'System', NOW(), NOW(), NOW()),
('Authentication', 'CookieExpireHours', '8', 'int', 'Authentication cookie expiration in hours', '8', false, true, NULL, true, 3, '{"min":1,"max":24}', 'System', NOW(), NOW(), NOW()),
('Authentication', 'InactivityCheckIntervalSeconds', '10', 'int', 'How often to check for user inactivity (seconds)', '10', false, false, NULL, true, 4, '{"min":5,"max":120}', 'System', NOW(), NOW(), NOW()),

-- ==================== ImageAnalysis ====================
('ImageAnalysis', 'MaxGroupsPerCycle', '500', 'int', 'Max groups processed per orchestrator cycle', '500', false, false, NULL, true, 1, '{"min":10,"max":5000}', 'System', NOW(), NOW(), NOW()),
('ImageAnalysis', 'MaxCompletenessRowsPerCycle', '5000', 'int', 'Max completeness rows processed per cycle', '5000', false, false, NULL, true, 2, '{"min":100,"max":50000}', 'System', NOW(), NOW(), NOW()),
('ImageAnalysis', 'MaxExecutionTimeMinutes', '2', 'int', 'Max execution time per orchestrator cycle (minutes)', '2', false, false, NULL, true, 3, '{"min":1,"max":30}', 'System', NOW(), NOW(), NOW()),
('ImageAnalysis', 'MaxIdleMinutesForReadiness', '60', 'int', 'Max idle minutes before a user is marked not ready', '60', false, false, NULL, true, 4, '{"min":5,"max":480}', 'System', NOW(), NOW(), NOW()),
('ImageAnalysis', 'OutboxPath', 'C:\ICUMS Submissions\ImageAnalysis\Outbox', 'string', 'File path for ICUMS submission outbox', 'C:\ICUMS Submissions\ImageAnalysis\Outbox', false, true, NULL, true, 5, NULL, 'System', NOW(), NOW(), NOW()),

-- ==================== Validation ====================
('Validation', 'CompletenessValidatedThreshold', '100', 'int', 'Overall completeness % to mark as Validated', '100', false, false, NULL, true, 1, '{"min":50,"max":100}', 'System', NOW(), NOW(), NOW()),
('Validation', 'CompletenessInReviewThreshold', '66', 'int', 'Overall completeness % to mark as InReview', '66', false, false, NULL, true, 2, '{"min":20,"max":99}', 'System', NOW(), NOW(), NOW()),
('Validation', 'ScannerValidatedThreshold', '90', 'int', 'Scanner data completeness % for Validated status', '90', false, false, NULL, true, 3, '{"min":50,"max":100}', 'System', NOW(), NOW(), NOW()),
('Validation', 'ScannerInReviewThreshold', '70', 'int', 'Scanner data completeness % for InReview status', '70', false, false, NULL, true, 4, '{"min":20,"max":99}', 'System', NOW(), NOW(), NOW()),
('Validation', 'ScannerDataCompleteThreshold', '90', 'int', 'Scanner data completeness % to consider data complete', '90', false, false, NULL, true, 5, '{"min":50,"max":100}', 'System', NOW(), NOW(), NOW()),
('Validation', 'ScannerReadyForSubmissionThreshold', '90', 'int', 'Scanner completeness % to be ready for submission', '90', false, false, NULL, true, 6, '{"min":50,"max":100}', 'System', NOW(), NOW(), NOW()),
('Validation', 'ICUMSCompleteThreshold', '75', 'int', 'ICUMS data completeness % for clearance type determination', '75', false, false, NULL, true, 7, '{"min":30,"max":100}', 'System', NOW(), NOW(), NOW()),
('Validation', 'ReadyForSubmissionThreshold', '90', 'int', 'Overall completeness % to be ready for ICUMS submission', '90', false, false, NULL, true, 8, '{"min":50,"max":100}', 'System', NOW(), NOW(), NOW()),

-- ==================== ICUMS ====================
('ICUMS', 'CircuitBreakerTimeoutMinutes', '5', 'int', 'Circuit breaker timeout duration in minutes', '5', false, false, NULL, true, 1, '{"min":1,"max":60}', 'System', NOW(), NOW(), NOW()),
('ICUMS', 'CacheTimeoutMinutes', '10', 'int', 'ICUMS API response cache timeout in minutes', '10', false, false, NULL, true, 2, '{"min":1,"max":120}', 'System', NOW(), NOW(), NOW()),
('ICUMS', 'HttpClientTimeoutMinutes', '5', 'int', 'ICUMS HTTP client request timeout in minutes', '5', false, false, NULL, true, 3, '{"min":1,"max":30}', 'System', NOW(), NOW(), NOW()),
('ICUMS', 'DownloadsPath', 'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads', 'string', 'ICUMS downloads directory path', 'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads', false, true, NULL, true, 4, NULL, 'System', NOW(), NOW(), NOW()),
('ICUMS', 'Backup.Directory', 'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Backup', 'string', 'ICUMS backup directory path', 'C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Backup', false, true, NULL, true, 5, NULL, 'System', NOW(), NOW(), NOW()),

-- ==================== Caching ====================
('Caching', 'PermissionsExpirationHours', '8', 'int', 'How long permission data is cached (hours)', '8', false, false, NULL, true, 1, '{"min":1,"max":48}', 'System', NOW(), NOW(), NOW()),
('Caching', 'RolesExpirationMinutes', '15', 'int', 'How long role data is cached (minutes)', '15', false, false, NULL, true, 2, '{"min":1,"max":120}', 'System', NOW(), NOW(), NOW()),
('Caching', 'ReadyGroupsCache.ExpirationSeconds', '30', 'int', 'Ready groups cache expiration (seconds)', '30', false, false, NULL, true, 3, '{"min":5,"max":300}', 'System', NOW(), NOW(), NOW()),
('Caching', 'ReadyGroupsCache.MaxGroups', '200', 'int', 'Max groups loaded into ready groups cache', '200', false, false, NULL, true, 4, '{"min":10,"max":2000}', 'System', NOW(), NOW(), NOW()),
('Caching', 'ContainerListMinutes', '5', 'int', 'Container list cache duration (minutes)', '5', false, false, NULL, true, 5, '{"min":1,"max":60}', 'System', NOW(), NOW(), NOW()),
('Caching', 'ContainerDetailsMinutes', '10', 'int', 'Container details cache duration (minutes)', '10', false, false, NULL, true, 6, '{"min":1,"max":60}', 'System', NOW(), NOW(), NOW()),
('Caching', 'DashboardMinutes', '1', 'int', 'Dashboard data cache duration (minutes)', '1', false, false, NULL, true, 7, '{"min":1,"max":30}', 'System', NOW(), NOW(), NOW()),
('Caching', 'StaticDataHours', '1', 'int', 'Static data cache duration (hours)', '1', false, false, NULL, true, 8, '{"min":1,"max":24}', 'System', NOW(), NOW(), NOW()),

-- ==================== UI ====================
('UI', 'AutoRefreshSeconds', '30', 'int', 'Auto-refresh interval for operation pages (seconds)', '30', false, false, NULL, true, 1, '{"min":5,"max":300}', 'System', NOW(), NOW(), NOW()),
('UI', 'DashboardRefreshSeconds', '60', 'int', 'Auto-refresh interval for dashboard pages (seconds)', '60', false, false, NULL, true, 2, '{"min":10,"max":600}', 'System', NOW(), NOW(), NOW()),
('UI', 'DefaultPageSize', '50', 'int', 'Default number of rows per page in tables', '50', false, false, NULL, true, 3, '{"min":10,"max":500}', 'System', NOW(), NOW(), NOW()),

-- ==================== Alerts ====================
('Alerts', 'DiskUsageWarningPercent', '80', 'int', 'Disk usage % threshold for warning alerts', '80', false, false, NULL, true, 1, '{"min":50,"max":95}', 'System', NOW(), NOW(), NOW()),
('Alerts', 'DiskUsageCriticalPercent', '90', 'int', 'Disk usage % threshold for critical alerts', '90', false, false, NULL, true, 2, '{"min":60,"max":99}', 'System', NOW(), NOW(), NOW()),
('Alerts', 'QueueHealth.SuccessRateWarning', '95', 'int', 'Queue success rate % below which a warning is raised', '95', false, false, NULL, true, 3, '{"min":50,"max":100}', 'System', NOW(), NOW(), NOW()),
('Alerts', 'QueueHealth.SuccessRateCritical', '99', 'int', 'Queue success rate % below which a critical alert is raised', '99', false, false, NULL, true, 4, '{"min":60,"max":100}', 'System', NOW(), NOW(), NOW()),
('Alerts', 'Dashboard.QualityScoreWarning', '70', 'int', 'Quality score below which a warning is shown', '70', false, false, NULL, true, 5, '{"min":20,"max":100}', 'System', NOW(), NOW(), NOW()),
('Alerts', 'Dashboard.QualityScoreCritical', '50', 'int', 'Quality score below which a critical alert is shown', '50', false, false, NULL, true, 6, '{"min":10,"max":100}', 'System', NOW(), NOW(), NOW()),
('Alerts', 'Dashboard.ErrorRateCritical', '5', 'int', 'Error rate % above which a critical alert is raised', '5', false, false, NULL, true, 7, '{"min":1,"max":50}', 'System', NOW(), NOW(), NOW()),

-- ==================== Capacity ====================
('Capacity', 'AnalystThroughputPerPerson', '10', 'int', 'Expected number of groups an analyst can process per shift', '10', false, false, NULL, true, 1, '{"min":1,"max":100}', 'System', NOW(), NOW(), NOW()),
('Capacity', 'AuditorThroughputPerPerson', '20', 'int', 'Expected number of groups an auditor can process per shift', '20', false, false, NULL, true, 2, '{"min":1,"max":200}', 'System', NOW(), NOW(), NOW()),

-- ==================== HealthChecks ====================
('HealthChecks', 'ApiHealthUrl', 'http://localhost:5205/health', 'string', 'URL used by internal health check to verify API is running', 'http://localhost:5205/health', false, true, NULL, true, 1, NULL, 'System', NOW(), NOW(), NOW()),
('HealthChecks', 'InternetCheckUrl', 'https://www.google.com', 'string', 'URL used to verify internet connectivity', 'https://www.google.com', false, false, NULL, true, 2, NULL, 'System', NOW(), NOW(), NOW()),

-- ==================== Email ====================
('Email', 'SmtpTimeoutMs', '30000', 'int', 'SMTP client timeout in milliseconds', '30000', false, false, NULL, true, 1, '{"min":5000,"max":120000}', 'System', NOW(), NOW(), NOW()),

-- ==================== CargoGroup ====================
('CargoGroup', 'RequestTimeoutSeconds', '55', 'int', 'Timeout for cargo group API requests (seconds)', '55', false, false, NULL, true, 1, '{"min":10,"max":300}', 'System', NOW(), NOW(), NOW()),

-- ==================== ApiSettings ====================
('ApiSettings', 'HttpClientTimeoutSeconds', '90', 'int', 'WebApp HTTP client timeout for API calls (seconds)', '90', false, true, NULL, true, 1, '{"min":10,"max":600}', 'System', NOW(), NOW(), NOW())

ON CONFLICT DO NOTHING;
