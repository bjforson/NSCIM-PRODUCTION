using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.Permissions;

namespace NickScanCentralImagingPortal.Services.AccessReview
{
    /// <summary>
    /// Access Review Background Service - ISO 27001 compliance requirement
    /// Automatically performs quarterly access reviews and generates reports
    /// </summary>
    public class AccessReviewService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccessReviewService> _logger;
        private const string SERVICE_ID = "[ACCESS-REVIEW]";
        private DateTime _lastReviewDate = DateTime.MinValue;
        private readonly TimeSpan _reviewInterval = TimeSpan.FromDays(90); // Quarterly reviews

        public AccessReviewService(
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration,
            ILogger<AccessReviewService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait for application to fully start
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            _logger.LogInformation("{ServiceId} Access Review Service started. Review interval: {Interval}",
                SERVICE_ID, _reviewInterval);

            // Check if service is enabled
            var isEnabled = _configuration.GetValue<bool>("AccessReview:Enabled", true);
            if (!isEnabled)
            {
                _logger.LogWarning("{ServiceId} Access Review Service is DISABLED in configuration", SERVICE_ID);
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check if it's time for a review (quarterly)
                    var timeSinceLastReview = DateTime.UtcNow - _lastReviewDate;
                    if (timeSinceLastReview >= _reviewInterval || _lastReviewDate == DateTime.MinValue)
                    {
                        await PerformAccessReviewAsync(stoppingToken);
                    }
                    else
                    {
                        _logger.LogDebug("{ServiceId} Next review in {Days} days", SERVICE_ID,
                            (_reviewInterval - timeSinceLastReview).Days);
                    }

                    // Check daily instead of waiting 90 days (Task.Delay has max limit)
                    await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Error during access review", SERVICE_ID);

                    // Wait 24 hours before retrying on error
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
            }
        }

        private async Task PerformAccessReviewAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("{ServiceId} Starting quarterly access review", SERVICE_ID);

            using var scope = _serviceScopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

            try
            {
                // Get all active users
                var users = await userRepository.GetActiveUsersAsync();
                _logger.LogInformation("{ServiceId} Reviewing access for {Count} active users", SERVICE_ID, users.Count);

                var reviewResults = new List<AccessReviewResult>();
                var issuesFound = 0;

                foreach (var user in users)
                {
                    var reviewResult = await ReviewUserAccessAsync(
                        user,
                        userRepository,
                        roleService,
                        permissionService,
                        cancellationToken);

                    reviewResults.Add(reviewResult);

                    if (reviewResult.HasIssues)
                    {
                        issuesFound++;
                        _logger.LogWarning("{ServiceId} Access review issues found for user {Username} ({UserId}): {Issues}",
                            SERVICE_ID, user.Username, user.Id, string.Join(", ", reviewResult.Issues));
                    }
                }

                // Generate summary report
                var summary = new
                {
                    ReviewDate = DateTime.UtcNow,
                    TotalUsers = users.Count,
                    UsersWithIssues = issuesFound,
                    UsersWithNoRecentLogin = reviewResults.Count(r => r.DaysSinceLastLogin > 90),
                    UsersWithExcessivePermissions = reviewResults.Count(r => r.PermissionCount > 50),
                    UsersWithInactiveAccess = reviewResults.Count(r => !r.HasRecentActivity && r.DaysSinceLastLogin > 180)
                };

                _logger.LogInformation("{ServiceId} Access review completed: {Summary}",
                    SERVICE_ID, System.Text.Json.JsonSerializer.Serialize(summary));

                // Log review results
                _logger.LogInformation("{ServiceId} Review Results: {IssuesFound} users with issues, {NoLogin} users with no recent login, {ExcessivePerms} users with excessive permissions",
                    SERVICE_ID, issuesFound,
                    reviewResults.Count(r => r.DaysSinceLastLogin > 90),
                    reviewResults.Count(r => r.PermissionCount > 50));

                // TODO: Send notification to administrators if issues found
                if (issuesFound > 0)
                {
                    _logger.LogWarning("{ServiceId} ⚠️ {Count} users require access review attention", SERVICE_ID, issuesFound);
                }

                _lastReviewDate = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error performing access review", SERVICE_ID);
                throw;
            }
        }

        private async Task<AccessReviewResult> ReviewUserAccessAsync(
            NickScanCentralImagingPortal.Core.Models.User user,
            IUserRepository userRepository,
            IRoleService roleService,
            IPermissionService permissionService,
            CancellationToken cancellationToken)
        {
            var result = new AccessReviewResult
            {
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                Department = user.Department,
                RoleName = user.Role.ToString(),
                IsActive = user.IsActive,
                LastLoginDate = user.LastLoginAt,
                DaysSinceLastLogin = user.LastLoginAt.HasValue
                    ? (DateTime.UtcNow - user.LastLoginAt.Value).Days
                    : (int?)null
            };

            try
            {
                // Get user roles - users have a single AssignedRole
                var roles = new List<NickScanCentralImagingPortal.Core.Entities.Role>();
                if (user.AssignedRole != null)
                {
                    roles.Add(user.AssignedRole);
                    result.Roles = new List<string> { user.AssignedRole.Name };
                }
                else
                {
                    // If AssignedRole is not loaded, try to get it from the role service
                    if (user.RoleId.HasValue)
                    {
                        var role = await roleService.GetRoleByIdAsync(user.RoleId.Value);
                        if (role != null)
                        {
                            roles.Add(role);
                            result.Roles = new List<string> { role.Name };
                        }
                    }
                }
                result.RoleCount = roles.Count;

                // Get user permissions
                var permissionNames = await permissionService.GetUserPermissionsAsync(user.Id);
                result.Permissions = permissionNames.ToList();
                result.PermissionCount = permissionNames.Count;

                // Check for issues
                result.Issues = new List<string>();

                // Issue 1: No recent login (90+ days)
                if (!user.LastLoginAt.HasValue || (DateTime.UtcNow - user.LastLoginAt.Value).Days > 90)
                {
                    result.Issues.Add($"No login in {(user.LastLoginAt.HasValue ? (DateTime.UtcNow - user.LastLoginAt.Value).Days : 999)} days");
                }

                // Issue 2: Excessive permissions (50+)
                if (permissionNames.Count > 50)
                {
                    result.Issues.Add($"Excessive permissions ({permissionNames.Count} permissions)");
                }

                // Issue 3: Inactive user with access
                if (!user.LastLoginAt.HasValue || (DateTime.UtcNow - user.LastLoginAt.Value).Days > 180)
                {
                    result.Issues.Add("User appears inactive but still has access");
                }

                // Issue 4: Multiple roles (potential separation of duties issue)
                // Note: Users have a single role, but checking for consistency
                if (roles.Count > 1)
                {
                    result.Issues.Add($"Multiple roles assigned ({roles.Count} roles)");
                }

                result.HasIssues = result.Issues.Any();
                result.HasRecentActivity = user.LastLoginAt.HasValue &&
                                          (DateTime.UtcNow - user.LastLoginAt.Value).Days <= 30;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error reviewing access for user {UserId}", SERVICE_ID, user.Id);
                result.Issues.Add("Error during review");
                result.HasIssues = true;
            }

            return result;
        }
    }

    // Helper class for access review results
    internal class AccessReviewResult
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Department { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? LastLoginDate { get; set; }
        public int? DaysSinceLastLogin { get; set; }
        public List<string> Roles { get; set; } = new();
        public int RoleCount { get; set; }
        public List<string> Permissions { get; set; } = new();
        public int PermissionCount { get; set; }
        public List<string> Issues { get; set; } = new();
        public bool HasIssues { get; set; }
        public bool HasRecentActivity { get; set; }
    }
}

