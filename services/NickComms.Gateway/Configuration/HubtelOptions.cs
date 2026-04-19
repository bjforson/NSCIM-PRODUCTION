namespace NickComms.Gateway.Configuration;

public class HubtelOptions
{
    public const string SectionName = "Hubtel";

    public string SmsBaseUrl { get; set; } = "https://smsc.hubtel.com";
    public string OtpBaseUrl { get; set; } = "https://api-otp.hubtel.com";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string DefaultSenderId { get; set; } = "NSCIM";
}
