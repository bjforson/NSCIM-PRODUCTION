using System.Text.Json.Serialization;

namespace NickComms.Gateway.Models;

// --- SMS Models ---

public class HubtelSendRequest
{
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class HubtelSendResponse
{
    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("statusDescription")]
    public string? StatusDescription { get; set; }

    [JsonPropertyName("networkId")]
    public string? NetworkId { get; set; }
}

public class HubtelStatusResponse
{
    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    [JsonPropertyName("units")]
    public int Units { get; set; }

    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("clientReference")]
    public string? ClientReference { get; set; }

    [JsonPropertyName("networkId")]
    public string? NetworkId { get; set; }

    [JsonPropertyName("updateTime")]
    public string? UpdateTime { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }
}

// --- OTP Models ---

public class HubtelOtpSendRequest
{
    [JsonPropertyName("senderId")]
    public string SenderId { get; set; } = string.Empty;

    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = "GH";
}

public class HubtelOtpSendResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public HubtelOtpData? Data { get; set; }
}

public class HubtelOtpData
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;
}

public class HubtelOtpVerifyRequest
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

public class HubtelOtpResendRequest
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;
}
