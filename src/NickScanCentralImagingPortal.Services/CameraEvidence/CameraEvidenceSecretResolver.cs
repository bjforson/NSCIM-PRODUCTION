using Microsoft.Extensions.Configuration;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public sealed class CameraEvidenceSecretResolver : ICameraEvidenceSecretResolver
    {
        private readonly IConfiguration _configuration;

        public CameraEvidenceSecretResolver(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string? Resolve(string? secretName, string? inlineValue = null)
        {
            if (!string.IsNullOrWhiteSpace(inlineValue) && !LooksLikePlaceholder(inlineValue))
            {
                return inlineValue;
            }

            if (string.IsNullOrWhiteSpace(secretName))
            {
                return null;
            }

            var candidates = new[]
            {
                secretName,
                $"NICKSCAN_{secretName}",
                $"NICKSCAN_{NormalizeForEnvironment(secretName)}",
                $"Secrets:{secretName}",
                $"CameraEvidence:Secrets:{secretName}"
            };

            foreach (var candidate in candidates)
            {
                var value = Environment.GetEnvironmentVariable(candidate) ?? _configuration[candidate];
                if (!string.IsNullOrWhiteSpace(value) && !LooksLikePlaceholder(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool LooksLikePlaceholder(string value)
        {
            return value.Contains("***USE_ENV", StringComparison.OrdinalIgnoreCase)
                || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeForEnvironment(string value)
        {
            var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_').ToArray();
            return new string(chars);
        }
    }
}
