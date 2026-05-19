using System.ComponentModel.DataAnnotations;

namespace NickComms.Gateway.Models;

// ===================== SMS =====================

public class SendSmsRequest
{
    [Required]
    public string To { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public string? From { get; set; }
    public string? ClientReference { get; set; }
}

public class BulkSmsRequest
{
    [Required]
    [MinLength(1)]
    public string[] Recipients { get; set; } = [];

    [Required]
    public string Content { get; set; } = string.Empty;

    public string? From { get; set; }
    public string? ClientReference { get; set; }
}

public class SmsResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? HubtelMessageId { get; set; }
    public decimal? Rate { get; set; }
}

public class BulkSmsResponse
{
    public Guid BatchId { get; set; }
    public int AcceptedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SmsStatusResponse
{
    public Guid Id { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? HubtelMessageId { get; set; }
    public string? HubtelStatus { get; set; }
    public decimal? Rate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }
}

// ===================== EMAIL =====================

public class SendEmailRequest
{
    [Required]
    [EmailAddress]
    public string To { get; set; } = string.Empty;

    public string? ToName { get; set; }

    [Required]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    /// <summary>If true, body is treated as HTML.</summary>
    public bool IsHtml { get; set; }

    public string? FromEmail { get; set; }
    public string? FromName { get; set; }
    public string? ClientReference { get; set; }

    /// <summary>Optional file attachments (max ~10 MB combined).</summary>
    public List<EmailAttachment>? Attachments { get; set; }
}

public class EmailAttachment
{
    [Required]
    public string Filename { get; set; } = string.Empty;

    /// <summary>Base64-encoded file content.</summary>
    [Required]
    public string ContentBase64 { get; set; } = string.Empty;

    /// <summary>MIME type, e.g. "application/pdf".</summary>
    public string ContentType { get; set; } = "application/octet-stream";
}

public class BulkEmailRequest
{
    [Required]
    [MinLength(1)]
    public EmailRecipient[] Recipients { get; set; } = [];

    [Required]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public bool IsHtml { get; set; }
    public string? FromEmail { get; set; }
    public string? FromName { get; set; }
    public string? ClientReference { get; set; }

    /// <summary>Optional file attachments applied to every recipient.</summary>
    public List<EmailAttachment>? Attachments { get; set; }
}

public class EmailRecipient
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? Name { get; set; }
}

public class EmailResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool DuplicateSuppressed { get; set; }
}

public class BulkEmailResponse
{
    public Guid BatchId { get; set; }
    public int AcceptedCount { get; set; }
    public int DuplicateSuppressedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class EmailStatusResponse
{
    public Guid Id { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }
}

// ===================== OTP =====================

public class SendOtpRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class VerifyOtpRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;
}

public class ResendOtpRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class OtpSendResponse
{
    public string Message { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
}

public class OtpVerifyResponse
{
    public bool Verified { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class OtpResendResponse
{
    public string Message { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
}

// ===================== HISTORY (unified) =====================

public class MessageHistoryResponse
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<MessageHistoryItem> Items { get; set; } = [];
}

public class MessageHistoryItem
{
    public Guid Id { get; set; }
    public string Channel { get; set; } = string.Empty; // "sms" or "email"
    public string Recipient { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ClientApp { get; set; } = string.Empty;
    public Guid? BatchId { get; set; }
    public decimal? Rate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}
