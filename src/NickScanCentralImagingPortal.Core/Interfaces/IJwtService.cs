using System.Security.Claims;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for JWT token generation and validation
    /// </summary>
    public interface IJwtService
    {
        /// <summary>
        /// Generate a JWT token for the authenticated user
        /// </summary>
        /// <param name="user">The user to generate token for</param>
        /// <returns>JWT token string</returns>
        string GenerateToken(User user);

        /// <summary>
        /// Generate a refresh token for token renewal
        /// </summary>
        /// <returns>Refresh token string</returns>
        string GenerateRefreshToken();

        /// <summary>
        /// Validate a JWT token and extract claims
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <returns>ClaimsPrincipal if valid, null otherwise</returns>
        ClaimsPrincipal? ValidateToken(string token);

        /// <summary>
        /// Refresh an existing JWT token using a refresh token
        /// </summary>
        /// <param name="refreshToken">The refresh token</param>
        /// <returns>New JWT token if valid, null otherwise</returns>
        Task<string?> RefreshTokenAsync(string refreshToken);
    }
}

