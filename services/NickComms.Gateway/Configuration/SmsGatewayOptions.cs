namespace NickComms.Gateway.Configuration;

public class SmsGatewayOptions
{
    public const string SectionName = "SmsGateway";

    /// <summary>Seconds between each SMS sent to Hubtel. 12s = 5 msgs/min.</summary>
    public int DrainIntervalSeconds { get; set; } = 12;

    /// <summary>Max Polly retries on transient Hubtel failures.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Default ISO country code for OTP.</summary>
    public string DefaultCountryCode { get; set; } = "GH";
}
