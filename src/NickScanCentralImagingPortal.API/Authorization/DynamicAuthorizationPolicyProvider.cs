using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace NickScanCentralImagingPortal.API.Authorization
{
    /// <summary>
    /// Provides dynamic authorization policies for permission-based authorization
    /// Supports policies in format: "Permission:permission.name"
    /// </summary>
    public class DynamicAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new();

        public DynamicAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options) : base(options)
        {
        }

        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            // Check if it's a permission-based policy
            if (policyName.StartsWith("Permission:", StringComparison.OrdinalIgnoreCase))
            {
                var permissionName = policyName.Substring("Permission:".Length);

                // Return cached policy or create new one
                return _policies.GetOrAdd(policyName, name =>
                {
                    var policyBuilder = new AuthorizationPolicyBuilder();
                    policyBuilder.AddRequirements(new PermissionRequirement(permissionName));
                    return policyBuilder.Build();
                });
            }

            // Use base provider for other policies (e.g., "AdminOnly", "CustomsOfficer", etc.)
            return await base.GetPolicyAsync(policyName);
        }
    }
}

