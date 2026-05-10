using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NickScanCentralImagingPortal.API.Controllers;
using NickScanCentralImagingPortal.Core.DTOs.Settings;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Permissions;
using Xunit;

namespace NickScanCentralImagingPortal.Tests.Controllers
{
    public class SecurityHardeningControllerTests
    {
        [Fact]
        public async Task UpdateUser_WhenSelfUpdate_StripsPrivilegedFields()
        {
            var existingUser = new User
            {
                Id = 7,
                Username = "alice",
                Email = "alice@example.test",
                FirstName = "Alice",
                LastName = "Analyst",
                PasswordHash = "hash",
                RoleId = 1,
                IsActive = true
            };
            var repository = new CapturingUserRepository(existingUser);
            var controller = new UsersController(
                repository,
                new ThrowingRoleService(),
                new DenyingPermissionService(),
                NullLogger<UsersController>.Instance,
                new ConfigurationBuilder().Build())
            {
                ControllerContext = BuildControllerContext(7, "alice", "alice@example.test")
            };

            var result = await controller.UpdateUser(7, new UpdateUserRequest
            {
                Email = "alice@example.test",
                FirstName = "Alicia",
                LastName = "Analyst",
                Role = "Admin",
                RoleId = 99,
                IsActive = false,
                UpdatedBy = "client-supplied"
            });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<UserManagementDto>(ok.Value);

            Assert.Equal("Alicia", repository.CapturedUpdate?.FirstName);
            Assert.Null(repository.CapturedUpdate?.Role);
            Assert.Null(repository.CapturedUpdate?.RoleId);
            Assert.Null(repository.CapturedUpdate?.IsActive);
            Assert.Equal("alice", repository.CapturedUpdate?.UpdatedBy);
            Assert.Equal(7, dto.Id);
            Assert.Null(typeof(UserManagementDto).GetProperty("PasswordHash"));
        }

        [Fact]
        public async Task GetSetting_WhenSecretLike_ReturnsRedactedValue()
        {
            var settingsService = new FakeSettingsService(new SystemSettingDto
            {
                Category = "Email",
                SettingKey = "SmtpPassword",
                SettingValue = "raw-password",
                IsEncrypted = true
            });
            var controller = new SettingsController(
                settingsService,
                NullLogger<SettingsController>.Instance);

            var result = await controller.GetSetting("Email", "SmtpPassword");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<SystemSettingDto>(ok.Value);
            Assert.Equal("***REDACTED***", dto.SettingValue);
        }

        [Fact]
        public async Task UpdateSetting_WhenRedactedPlaceholderPosted_DoesNotOverwriteSecret()
        {
            var settingsService = new FakeSettingsService(new SystemSettingDto
            {
                Category = "Email",
                SettingKey = "SmtpPassword",
                SettingValue = "raw-password",
                IsEncrypted = true
            });
            var controller = new SettingsController(
                settingsService,
                NullLogger<SettingsController>.Instance);

            var result = await controller.UpdateSetting(new UpdateSettingDto
            {
                Category = "Email",
                SettingKey = "SmtpPassword",
                SettingValue = "***REDACTED***",
                ChangedBy = "admin"
            });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var dto = Assert.IsType<SystemSettingDto>(ok.Value);
            Assert.False(settingsService.UpdateCalled);
            Assert.Equal("***REDACTED***", dto.SettingValue);
        }

        [Fact]
        public void QueueHealth_DetailedEndpointsRequireAuthorization()
        {
            var controller = typeof(QueueHealthController);

            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.NotNull(controller.GetMethod(nameof(QueueHealthController.GetQueueHealth))?
                .GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.NotNull(controller.GetMethod(nameof(QueueHealthController.GetStatistics))?
                .GetCustomAttribute<AllowAnonymousAttribute>());

            Assert.NotNull(controller.GetMethod(nameof(QueueHealthController.GetStuckItems))?
                .GetCustomAttribute<AuthorizeAttribute>());
            Assert.NotNull(controller.GetMethod(nameof(QueueHealthController.GetFailedItems))?
                .GetCustomAttribute<AuthorizeAttribute>());
            Assert.NotNull(controller.GetMethod(nameof(QueueHealthController.GetPublishingHealth))?
                .GetCustomAttribute<AuthorizeAttribute>());
            Assert.NotNull(controller.GetMethod(nameof(QueueHealthController.GetQueueItems))?
                .GetCustomAttribute<AuthorizeAttribute>());
        }

        private static ControllerContext BuildControllerContext(int userId, string username, string email)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Email, email)
            }, "test");

            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            };
        }

        private sealed class CapturingUserRepository : IUserRepository
        {
            private readonly User _user;

            public CapturingUserRepository(User user)
            {
                _user = user;
            }

            public UpdateUserRequest? CapturedUpdate { get; private set; }

            public Task<User?> GetUserByIdAsync(int id)
            {
                return Task.FromResult<User?>(id == _user.Id ? _user : null);
            }

            public Task<User> UpdateUserAsync(int userId, UpdateUserRequest request)
            {
                CapturedUpdate = request;
                _user.FirstName = request.FirstName ?? _user.FirstName;
                _user.LastName = request.LastName ?? _user.LastName;
                _user.Email = request.Email ?? _user.Email;
                _user.UpdatedBy = request.UpdatedBy;
                return Task.FromResult(_user);
            }

            public Task<bool> EmailExistsAsync(string email) => Task.FromResult(false);
            public Task<User?> GetUserByUsernameAsync(string username) => throw new NotImplementedException();
            public Task<User?> GetUserByEmailAsync(string email) => throw new NotImplementedException();
            public Task<List<User>> GetAllUsersAsync() => throw new NotImplementedException();
            public Task<List<User>> GetActiveUsersAsync() => throw new NotImplementedException();
            public Task<User> CreateUserAsync(CreateUserRequest request) => throw new NotImplementedException();
            public Task<bool> DeleteUserAsync(int id) => throw new NotImplementedException();
            public Task<bool> ValidatePasswordAsync(string username, string password) => throw new NotImplementedException();
            public Task UpdateLastLoginAsync(int userId) => throw new NotImplementedException();
            public Task<bool> UsernameExistsAsync(string username) => throw new NotImplementedException();
            public Task<bool> UpdatePasswordAsync(int userId, string newPassword) => throw new NotImplementedException();
            public Task<Guid?> RotateSessionIdAsync(int userId) => throw new NotImplementedException();
            public Task<Guid?> GetCurrentSessionIdAsync(int userId) => throw new NotImplementedException();
        }

        private sealed class DenyingPermissionService : IPermissionService
        {
            public Task<bool> HasPermissionAsync(int userId, string permissionName) => Task.FromResult(false);
            public Task<bool> HasPermissionAsync(string username, string permissionName) => Task.FromResult(false);
            public bool HasPermission(UserRole userRole, UserRole requiredRole) => false;
            public bool HasPermission(UserRole userRole, string permissionName) => false;
            public Task<List<string>> GetUserPermissionsAsync(int userId) => Task.FromResult(new List<string>());
            public Task<List<string>> GetRolePermissionsAsync(int roleId) => Task.FromResult(new List<string>());
            public Task GrantPermissionToUserAsync(int userId, string permissionName, string grantedBy, DateTime? expiresAt = null, string? reason = null) => throw new NotImplementedException();
            public Task RevokePermissionFromUserAsync(int userId, string permissionName, string revokedBy, string? reason = null) => throw new NotImplementedException();
            public Task RemoveUserPermissionOverrideAsync(int userId, string permissionName) => throw new NotImplementedException();
            public Task<bool> HasAllPermissionsAsync(int userId, params string[] permissionNames) => Task.FromResult(false);
            public Task<bool> HasAnyPermissionAsync(int userId, params string[] permissionNames) => Task.FromResult(false);
            public Task<List<int>> GetExpiredUserPermissionsAsync() => Task.FromResult(new List<int>());
            public Task CleanupExpiredPermissionsAsync() => Task.CompletedTask;
        }

        private sealed class ThrowingRoleService : IRoleService
        {
            public Task<List<Role>> GetAllRolesAsync() => throw new NotImplementedException();
            public Task<Role?> GetRoleByIdAsync(int roleId) => throw new NotImplementedException();
            public Task<Role?> GetRoleByNameAsync(string roleName) => throw new NotImplementedException();
            public Task<Role> CreateRoleAsync(string name, string displayName, string description, string createdBy, UserRole? baseRole = null) => throw new NotImplementedException();
            public Task<Role> UpdateRoleAsync(int roleId, string displayName, string description, string updatedBy) => throw new NotImplementedException();
            public Task DeleteRoleAsync(int roleId, string deletedBy) => throw new NotImplementedException();
            public Task AssignPermissionToRoleAsync(int roleId, string permissionName, string grantedBy) => throw new NotImplementedException();
            public Task RemovePermissionFromRoleAsync(int roleId, string permissionName) => throw new NotImplementedException();
            public Task AssignPermissionsToRoleAsync(int roleId, List<string> permissionNames, string grantedBy) => throw new NotImplementedException();
            public Task ReplaceRolePermissionsAsync(int roleId, List<string> permissionNames, string updatedBy) => throw new NotImplementedException();
            public Task<List<Permission>> GetRolePermissionsAsync(int roleId) => throw new NotImplementedException();
            public Task AssignRoleToUserAsync(int userId, int roleId, string assignedBy) => throw new NotImplementedException();
            public Task RemoveRoleFromUserAsync(int userId) => throw new NotImplementedException();
            public Task<List<User>> GetUsersInRoleAsync(int roleId) => throw new NotImplementedException();
            public Task<Role> CloneRoleAsync(int sourceRoleId, string newName, string newDisplayName, string createdBy) => throw new NotImplementedException();
            public Task<bool> IsRoleNameAvailableAsync(string roleName, int? excludeRoleId = null) => throw new NotImplementedException();
            public Task<List<RoleResyncResult>> ResyncBaseRolePermissionsAsync(string updatedBy) => throw new NotImplementedException();
        }

        private sealed class FakeSettingsService : ISettingsService
        {
            private readonly SystemSettingDto _setting;

            public FakeSettingsService(SystemSettingDto setting)
            {
                _setting = setting;
            }

            public bool UpdateCalled { get; private set; }

            public Task<SystemSettingDto?> GetSettingAsync(string category, string key)
            {
                return Task.FromResult<SystemSettingDto?>(_setting);
            }

            public Task<SystemSettingDto> UpdateSettingAsync(UpdateSettingDto update, string? ipAddress = null)
            {
                UpdateCalled = true;
                return Task.FromResult(new SystemSettingDto
                {
                    Category = update.Category,
                    SettingKey = update.SettingKey,
                    SettingValue = update.SettingValue,
                    IsEncrypted = _setting.IsEncrypted
                });
            }

            public Task<List<SystemSettingDto>> GetSettingsByCategoryAsync(string category) => throw new NotImplementedException();
            public Task<CategorySettingsDto?> GetCategorySettingsAsync(string category) => throw new NotImplementedException();
            public Task<List<CategorySettingsDto>> GetAllCategoriesAsync() => throw new NotImplementedException();
            public Task<SystemSettingDto> CreateSettingAsync(SystemSettingDto setting, string createdBy) => throw new NotImplementedException();
            public Task<bool> DeleteSettingAsync(string category, string key, string deletedBy) => throw new NotImplementedException();
            public Task<List<SystemSettingDto>> BulkUpdateSettingsAsync(BulkSettingsUpdateDto bulkUpdate, string? ipAddress = null) => throw new NotImplementedException();
            public Task<List<SettingsHistoryDto>> GetSettingHistoryAsync(string category, string key, int limit = 50) => throw new NotImplementedException();
            public Task<List<SettingsHistoryDto>> GetRecentChangesAsync(int limit = 100) => throw new NotImplementedException();
            public Task<UserPreferenceDto?> GetUserPreferenceAsync(int userId, string key) => throw new NotImplementedException();
            public Task<List<UserPreferenceDto>> GetAllUserPreferencesAsync(int userId) => throw new NotImplementedException();
            public Task<UserPreferenceDto> SetUserPreferenceAsync(int userId, string key, string value, string dataType = "string") => throw new NotImplementedException();
            public Task<bool> DeleteUserPreferenceAsync(int userId, string key) => throw new NotImplementedException();
            public Task<SettingsValidationResult> ValidateSettingAsync(string category, string key, string value) => throw new NotImplementedException();
            public Task<SettingsValidationResult> ValidateCategorySettingsAsync(Dictionary<string, string> settings, string category) => throw new NotImplementedException();
            public Task<string> EncryptValueAsync(string value) => throw new NotImplementedException();
            public Task<string> DecryptValueAsync(string encryptedValue) => throw new NotImplementedException();
            public Task<SettingsExportDto> ExportSettingsAsync(string? category = null) => throw new NotImplementedException();
            public Task<bool> ImportSettingsAsync(SettingsExportDto export, string importedBy, bool overwriteExisting = false) => throw new NotImplementedException();
            public Task<bool> ResetToDefaultsAsync(string category, string resetBy) => throw new NotImplementedException();
            public Task<Dictionary<string, object>> GetSettingsAsConfigurationAsync(string category) => throw new NotImplementedException();
            public Task<NickScanCentralImagingPortal.Core.DTOs.Settings.ConnectionTestResult> TestConnectionAsync(string category) => throw new NotImplementedException();
            public Task ClearCacheAsync() => throw new NotImplementedException();
        }
    }
}
