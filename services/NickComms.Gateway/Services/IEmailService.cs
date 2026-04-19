using NickComms.Gateway.Models;

namespace NickComms.Gateway.Services;

public interface IEmailService
{
    Task<EmailResponse> SendSingleAsync(SendEmailRequest request, string clientApp, CancellationToken ct = default);
    Task<BulkEmailResponse> SendBulkAsync(BulkEmailRequest request, string clientApp, CancellationToken ct = default);
    Task<EmailStatusResponse?> GetStatusAsync(Guid id, CancellationToken ct = default);
}
