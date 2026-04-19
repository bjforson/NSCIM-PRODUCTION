using NickComms.Gateway.Models;

namespace NickComms.Gateway.Services;

public interface ISmsService
{
    Task<SmsResponse> SendSingleAsync(SendSmsRequest request, string clientApp, CancellationToken ct = default);
    Task<BulkSmsResponse> SendBulkAsync(BulkSmsRequest request, string clientApp, CancellationToken ct = default);
    Task<SmsStatusResponse?> GetStatusAsync(Guid id, CancellationToken ct = default);
}
