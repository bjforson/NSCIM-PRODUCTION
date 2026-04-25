using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetUserByIdAsync(int id);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<User?> GetUserByEmailAsync(string email);
        Task<List<User>> GetAllUsersAsync();
        Task<List<User>> GetActiveUsersAsync();
        Task<User> CreateUserAsync(CreateUserRequest request);
        Task<User> UpdateUserAsync(int userId, UpdateUserRequest request);
        Task<bool> DeleteUserAsync(int id);
        Task<bool> ValidatePasswordAsync(string username, string password);
        Task UpdateLastLoginAsync(int userId);
        Task<bool> UsernameExistsAsync(string username);
        Task<bool> EmailExistsAsync(string email);
        Task<bool> UpdatePasswordAsync(int userId, string newPassword);

        /// <summary>
        /// Rotate <c>User.CurrentSessionId</c> and return the new value. Used by
        /// the login flow to invalidate any other device's still-valid JWT for
        /// the same user (single-session enforcement).
        /// </summary>
        Task<Guid?> RotateSessionIdAsync(int userId);

        /// <summary>
        /// Read the current session id without modifying it. Used by the JWT
        /// validation hook (cached) to compare against the token's sid claim.
        /// </summary>
        Task<Guid?> GetCurrentSessionIdAsync(int userId);
    }
}
