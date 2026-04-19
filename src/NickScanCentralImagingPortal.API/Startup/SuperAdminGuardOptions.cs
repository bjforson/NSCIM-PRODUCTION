using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.API.Startup
{
    /// <summary>
    /// Configuration options for enforcing a healthy SuperAdmin account at application startup.
    /// </summary>
    public class SuperAdminGuardOptions
    {
        /// <summary>
        /// Enable or disable the guard entirely.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Username of the SuperAdmin account to enforce.
        /// Defaults to "superadmin".
        /// </summary>
        [Required]
        public string Username { get; set; } = "superadmin";

        /// <summary>
        /// Optional email to set if the user needs to be created.
        /// </summary>
        public string? Email { get; set; } = "superadmin@nickscan.local";

        /// <summary>
        /// Optional first name for newly created account.
        /// </summary>
        public string FirstName { get; set; } = "Super";

        /// <summary>
        /// Optional last name for newly created account.
        /// </summary>
        public string LastName { get; set; } = "Administrator";

        /// <summary>
        /// Provide the password directly (development convenience).
        /// Prefer the environment variable override for production.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Name of an environment variable that contains the SuperAdmin password.
        /// This takes precedence over the inline Password property.
        /// </summary>
        public string? PasswordEnvironmentVariable { get; set; }

        /// <summary>
        /// Reset the SuperAdmin password on every startup (recommended so it never drifts).
        /// </summary>
        public bool ResetPasswordOnStartup { get; set; } = true;

        /// <summary>
        /// Reactivate the account if it has been disabled.
        /// </summary>
        public bool ReactivateAccount { get; set; } = true;

        /// <summary>
        /// Assign the SuperAdmin system role if it is missing.
        /// </summary>
        public bool AssignRoleIfMissing { get; set; } = true;

        /// <summary>
        /// Ensure the account is present; if missing, create it.
        /// </summary>
        public bool CreateIfMissing { get; set; } = true;
    }
}

