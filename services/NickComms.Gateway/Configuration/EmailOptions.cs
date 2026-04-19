namespace NickComms.Gateway.Configuration;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpServer { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string FromEmail { get; set; } = "noreply@nickscan.com";
    public string FromName { get; set; } = "NickComms Gateway";

    /// <summary>Seconds between each email sent (rate limiter). 0 = no limit.</summary>
    public int DrainIntervalSeconds { get; set; } = 2;
}
