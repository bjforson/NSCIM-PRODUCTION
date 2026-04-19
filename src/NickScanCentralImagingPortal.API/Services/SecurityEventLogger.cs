namespace NickScanCentralImagingPortal.API.Services
{
    /// <summary>
    /// Security event logging service for audit trail
    /// </summary>
    public interface ISecurityEventLogger
    {
        void LogLogin(string username, bool success, string? ipAddress, string? userAgent);
        void LogLogout(string username, string? ipAddress);
        void LogFailedAuthorization(string username, string resource, string requiredRole);
        void LogTokenRefresh(string username, string? ipAddress);
        void LogPasswordChange(string username, string? changedBy);
        void LogUserCreation(string username, string createdBy);
        void LogUserDeletion(string username, string deletedBy);
        void LogRoleChange(string username, string oldRole, string newRole, string changedBy);
        void LogSuspiciousActivity(string activity, string? username, string? ipAddress);
    }

    /// <summary>
    /// Implementation of security event logger
    /// </summary>
    public class SecurityEventLogger : ISecurityEventLogger
    {
        private readonly ILogger<SecurityEventLogger> _logger;

        public SecurityEventLogger(ILogger<SecurityEventLogger> logger)
        {
            _logger = logger;
        }

        public void LogLogin(string username, bool success, string? ipAddress, string? userAgent)
        {
            if (success)
            {
                _logger.LogInformation(
                    "🔐 SECURITY: Successful login | User: {Username} | IP: {IpAddress} | UserAgent: {UserAgent}",
                    username, ipAddress ?? "Unknown", userAgent ?? "Unknown");
            }
            else
            {
                _logger.LogWarning(
                    "⚠️ SECURITY: Failed login attempt | User: {Username} | IP: {IpAddress} | UserAgent: {UserAgent}",
                    username, ipAddress ?? "Unknown", userAgent ?? "Unknown");
            }
        }

        public void LogLogout(string username, string? ipAddress)
        {
            _logger.LogInformation(
                "🔓 SECURITY: User logout | User: {Username} | IP: {IpAddress}",
                username, ipAddress ?? "Unknown");
        }

        public void LogFailedAuthorization(string username, string resource, string requiredRole)
        {
            _logger.LogWarning(
                "⛔ SECURITY: Authorization failed | User: {Username} | Resource: {Resource} | RequiredRole: {RequiredRole}",
                username, resource, requiredRole);
        }

        public void LogTokenRefresh(string username, string? ipAddress)
        {
            _logger.LogInformation(
                "🔄 SECURITY: Token refresh | User: {Username} | IP: {IpAddress}",
                username, ipAddress ?? "Unknown");
        }

        public void LogPasswordChange(string username, string? changedBy)
        {
            _logger.LogWarning(
                "🔑 SECURITY: Password changed | User: {Username} | ChangedBy: {ChangedBy}",
                username, changedBy ?? "Self");
        }

        public void LogUserCreation(string username, string createdBy)
        {
            _logger.LogInformation(
                "➕ SECURITY: User created | User: {Username} | CreatedBy: {CreatedBy}",
                username, createdBy);
        }

        public void LogUserDeletion(string username, string deletedBy)
        {
            _logger.LogWarning(
                "🗑️ SECURITY: User deleted | User: {Username} | DeletedBy: {DeletedBy}",
                username, deletedBy);
        }

        public void LogRoleChange(string username, string oldRole, string newRole, string changedBy)
        {
            _logger.LogWarning(
                "👤 SECURITY: Role changed | User: {Username} | OldRole: {OldRole} | NewRole: {NewRole} | ChangedBy: {ChangedBy}",
                username, oldRole, newRole, changedBy);
        }

        public void LogSuspiciousActivity(string activity, string? username, string? ipAddress)
        {
            _logger.LogWarning(
                "🚨 SECURITY: Suspicious activity detected | Activity: {Activity} | User: {Username} | IP: {IpAddress}",
                activity, username ?? "Anonymous", ipAddress ?? "Unknown");
        }
    }
}

