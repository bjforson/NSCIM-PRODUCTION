using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UserRepository> _logger;

        public UserRepository(ApplicationDbContext context, ILogger<UserRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<User?> GetUserByIdAsync(int id)
        {
            try
            {
                return await _context.Users.FindAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user by ID {UserId}", id);
                return null;
            }
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            try
            {
                return await _context.Users
                    .Include(u => u.AssignedRole) // Load navigation property for Role computation
                        .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
                    .Include(u => u.UserPermissions)
                        .ThenInclude(up => up.Permission)
                    .FirstOrDefaultAsync(u => u.Username == username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user by username {Username}", username);
                return null;
            }
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            try
            {
                return await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user by email {Email}", email);
                return null;
            }
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                return await _context.Users
                    .Include(u => u.AssignedRole)
                    .OrderBy(u => u.Username)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all users");
                return new List<User>();
            }
        }

        public async Task<List<User>> GetActiveUsersAsync()
        {
            try
            {
                return await _context.Users
                    .Include(u => u.AssignedRole)
                    .Where(u => u.IsActive)
                    .OrderBy(u => u.Username)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active users");
                return new List<User>();
            }
        }

        public async Task<User> CreateUserAsync(CreateUserRequest request)
        {
            try
            {
                // Validate password is provided
                if (string.IsNullOrWhiteSpace(request.Password))
                {
                    throw new ArgumentException("Password is required");
                }

                // Determine role assignment using RoleId (preferred) or Role name
                Role? roleEntity = null;
                UserRole roleEnum = UserRole.Viewer;

                if (request.RoleId.HasValue)
                {
                    roleEntity = await _context.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId.Value && r.IsActive);
                    if (roleEntity == null)
                    {
                        throw new ArgumentException($"Role with ID {request.RoleId.Value} does not exist or is inactive");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(request.Role))
                {
                    roleEntity = await _context.Roles.FirstOrDefaultAsync(r => r.Name == request.Role && r.IsActive);
                    if (roleEntity == null)
                    {
                        throw new ArgumentException($"Role '{request.Role}' does not exist or is inactive");
                    }
                }
                else
                {
                    throw new ArgumentException("Role is required");
                }

                if (roleEntity != null)
                {
                    roleEnum = roleEntity.BaseRole ?? UserRole.Viewer;
                }

                var resolvedRoleId = roleEntity?.Id ?? request.RoleId;
                if (!resolvedRoleId.HasValue)
                {
                    throw new InvalidOperationException("Unable to resolve a valid role assignment.");
                }

                // ✅ Generate unique UserNumber for anonymized reporting
                var userNumber = await GenerateUserNumberAsync();

                var user = new User
                {
                    Username = request.Username,
                    Email = request.Email,
                    PasswordHash = HashPassword(request.Password), // ✅ Use password from request
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    UserNumber = userNumber, // ✅ Auto-generate user number
                    Role = roleEnum, // Convert string to enum
                    RoleId = resolvedRoleId.Value,
                    IsActive = request.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = request.CreatedBy ?? "System"
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ Created user {Username} with role {Role} (RoleId: {RoleId})",
                    user.Username, user.Role, user.RoleId);
                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user {Username}", request.Username);
                throw;
            }
        }

        public async Task<User> UpdateUserAsync(int userId, UpdateUserRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    throw new ArgumentException("User not found");
                }

                _logger.LogInformation("🔍 UPDATE USER DEBUG - User {UserId} ({Username})", userId, user.Username);
                _logger.LogInformation("   Current Role: {CurrentRole} (RoleId: {CurrentRoleId})", user.Role, user.RoleId);
                _logger.LogInformation("   Requested Role: '{RequestRole}'", request.Role ?? "NULL");

                if (!string.IsNullOrEmpty(request.Email))
                    user.Email = request.Email;

                if (!string.IsNullOrEmpty(request.FirstName))
                    user.FirstName = request.FirstName;

                if (!string.IsNullOrEmpty(request.LastName))
                    user.LastName = request.LastName;

                // ✅ FIX: Update both Role enum AND RoleId when role changes
                if (request.RoleId.HasValue || !string.IsNullOrEmpty(request.Role))
                {
                    Role? roleEntity = null;
                    if (request.RoleId.HasValue)
                    {
                        roleEntity = await _context.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId.Value && r.IsActive);
                        if (roleEntity == null)
                        {
                            throw new ArgumentException($"Role with ID {request.RoleId.Value} does not exist or is inactive");
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(request.Role))
                    {
                        _logger.LogInformation("   Attempting to parse role: '{RequestRole}'", request.Role);
                        roleEntity = await _context.Roles.FirstOrDefaultAsync(r => r.Name == request.Role && r.IsActive);
                        if (roleEntity == null)
                        {
                            throw new ArgumentException($"Role '{request.Role}' does not exist or is inactive");
                        }
                    }

                    if (roleEntity != null)
                    {
                        user.Role = roleEntity.BaseRole ?? user.Role;
                        user.RoleId = roleEntity.Id;
                        _logger.LogInformation("✅ Updated user {UserId} role to {Role} (RoleId: {RoleId})",
                            userId, user.Role, user.RoleId);
                    }
                }

                if (!string.IsNullOrEmpty(request.Department))
                    user.Department = request.Department;

                if (!string.IsNullOrEmpty(request.PhoneNumber))
                    user.PhoneNumber = request.PhoneNumber;

                if (request.IsActive.HasValue)
                    user.IsActive = request.IsActive.Value;

                // Note: Password changes handled by UpdatePasswordAsync

                user.UpdatedAt = DateTime.UtcNow;
                user.UpdatedBy = request.UpdatedBy ?? "System";

                if (!user.RoleId.HasValue)
                {
                    var fallbackRole = await _context.Roles
                        .Where(r => r.IsActive && r.BaseRole == user.Role)
                        .OrderByDescending(r => r.IsSystemRole)
                        .FirstOrDefaultAsync();

                    if (fallbackRole == null)
                    {
                        throw new InvalidOperationException($"User {user.Username} does not have a valid role assignment.");
                    }

                    user.RoleId = fallbackRole.Id;
                }

                _logger.LogInformation("   About to save changes - Final Role: {FinalRole}, RoleId: {FinalRoleId}", user.Role, user.RoleId);

                // Force EF to track changes
                _context.Entry(user).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                var changes = await _context.SaveChangesAsync();
                _logger.LogInformation("   SaveChanges affected {ChangeCount} rows", changes);

                _logger.LogInformation("✅ User {UserId} updated successfully by {UpdatedBy}", userId, user.UpdatedBy);

                // Verify the update - detach and re-query
                _context.Entry(user).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                var verifyUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                _logger.LogInformation("   VERIFICATION - Role in DB: {DbRole}, RoleId in DB: {DbRoleId}", verifyUser?.Role, verifyUser?.RoleId);

                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return false;
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user {UserId}", id);
                return false;
            }
        }

        public async Task<bool> UpdatePasswordAsync(int userId, string newPassword)
        {
            try
            {
                var user = await _context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    _logger.LogWarning("Attempted to update password for non-existent user ID: {UserId}", userId);
                    return false;
                }

                user.PasswordHash = HashPassword(newPassword);
                user.UpdatedAt = DateTime.UtcNow;
                user.UpdatedBy = "PasswordReset";

                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ Password updated for user {Username} (ID: {UserId})", user.Username, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating password for user {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Validate password with automatic migration from legacy SHA256 to BCrypt
        /// </summary>
        public async Task<bool> ValidatePasswordAsync(string username, string password)
        {
            try
            {
                var user = await _context.Users
                    .AsTracking()
                    .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

                if (user == null)
                {
                    return false;
                }

                bool isValid = VerifyPassword(password, user.PasswordHash);

                if (!isValid)
                {
                    return false;
                }

                // ✅ AUTOMATIC MIGRATION: If user has legacy SHA256 hash, upgrade to BCrypt
                if (IsLegacyHash(user.PasswordHash))
                {
                    _logger.LogInformation(
                        "🔒 Migrating user {Username} from legacy SHA256 to BCrypt hash",
                        username);

                    // Rehash password with BCrypt
                    user.PasswordHash = HashPassword(password);
                    user.UpdatedAt = DateTime.UtcNow;
                    user.UpdatedBy = "PasswordMigration";

                    await _context.SaveChangesAsync();

                    _logger.LogInformation(
                        "✅ Successfully migrated user {Username} to BCrypt",
                        username);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating password for user {Username}", username);
                return false;
            }
        }

        public async Task UpdateLastLoginAsync(int userId)
        {
            try
            {
                var user = await _context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user != null)
                {
                    user.LastLoginAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last login for user {UserId}", userId);
            }
        }

        public async Task<bool> UsernameExistsAsync(string username)
        {
            try
            {
                return await _context.Users
                    .AnyAsync(u => u.Username == username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking username existence {Username}", username);
                return false;
            }
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            try
            {
                return await _context.Users
                    .AnyAsync(u => u.Email == email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email existence {Email}", email);
                return false;
            }
        }

        /// <summary>
        /// Hash password using BCrypt with work factor 12
        /// Work factor 12 = ~300ms per hash (good balance between security and UX)
        /// </summary>
        private string HashPassword(string password)
        {
            // BCrypt automatically salts and uses adaptive hashing (slow by design)
            // This protects against rainbow table attacks and brute force attacks
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        /// <summary>
        /// Verify password against BCrypt hash (with migration support for legacy SHA256)
        /// </summary>
        private bool VerifyPassword(string password, string hash)
        {
            try
            {
                // Try BCrypt verification first (new format)
                if (hash.StartsWith("$2"))
                {
                    return BCrypt.Net.BCrypt.Verify(password, hash);
                }

                // Legacy SHA256 format detected - verify but DON'T auto-migrate here
                // Migration happens in ValidatePasswordAsync for active logins
                _logger.LogWarning("Legacy SHA256 password hash detected - user should reset password");
                return ComputeLegacySHA256Hash(password) == hash;
            }
            catch (BCrypt.Net.SaltParseException)
            {
                _logger.LogWarning("Invalid hash format for password verification");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying password");
                return false;
            }
        }

        /// <summary>
        /// Generate unique user number for anonymized reporting (e.g., USR-00001)
        /// </summary>
        private async Task<string> GenerateUserNumberAsync()
        {
            try
            {
                // Get the highest existing user number
                var maxUserNumber = await _context.Users
                    .Where(u => u.UserNumber != null && u.UserNumber.StartsWith("USR-"))
                    .Select(u => u.UserNumber)
                    .OrderByDescending(n => n)
                    .FirstOrDefaultAsync();

                int nextNumber = 1;
                if (!string.IsNullOrEmpty(maxUserNumber))
                {
                    // Extract number from format "USR-00001"
                    var numberPart = maxUserNumber.Substring(4); // Skip "USR-"
                    if (int.TryParse(numberPart, out var parsedNumber))
                    {
                        nextNumber = parsedNumber + 1;
                    }
                }

                return $"USR-{nextNumber:D5}"; // Format as USR-00001, USR-00002, etc.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating user number, using timestamp fallback");
                // Fallback: Use timestamp-based number if database query fails
                return $"USR-{DateTime.UtcNow:yyyyMMddHHmmss}";
            }
        }

        /// <summary>
        /// Legacy SHA256 hash computation (for backward compatibility only)
        /// DO NOT use for new passwords - kept only for migration
        /// </summary>
        private string ComputeLegacySHA256Hash(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        /// <summary>
        /// Check if password hash is using legacy SHA256 format
        /// BCrypt hashes always start with $2a$, $2b$, or $2y$
        /// </summary>
        private bool IsLegacyHash(string hash)
        {
            return !hash.StartsWith("$2");
        }
    }
}
