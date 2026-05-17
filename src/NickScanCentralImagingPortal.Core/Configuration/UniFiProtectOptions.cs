namespace NickScanCentralImagingPortal.Core.Configuration
{
    public sealed class UniFiProtectOptions
    {
        public const string SectionName = "UniFiProtect";

        public string ApiKeyHeader { get; set; } = "X-API-KEY";
        public string WebhookSecretHeader { get; set; } = "X-NSCIM-Webhook-Secret";
        public List<UniFiProtectSiteOptions> Sites { get; set; } = new();
    }

    public sealed class UniFiProtectSiteOptions
    {
        public string SiteKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiKeySecretName { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
        public string WebhookSecretName { get; set; } = string.Empty;
        public List<string> AllowedWebhookSourceCidrs { get; set; } = new();
        public bool VerifySsl { get; set; } = true;
        public int RequestTimeoutSeconds { get; set; } = 10;
        public bool IsEnabled { get; set; }
    }
}
