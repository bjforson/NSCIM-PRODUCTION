using NickComms.Gateway.Models;

namespace NickComms.Gateway.Services;

public interface IHubtelClient
{
    Task<HubtelSendResponse?> SendSmsAsync(string from, string to, string content, CancellationToken ct = default);
    Task<HubtelStatusResponse?> GetSmsStatusAsync(string messageId, CancellationToken ct = default);
    Task<HubtelOtpSendResponse?> SendOtpAsync(string senderId, string phoneNumber, string countryCode, CancellationToken ct = default);
    Task<bool> VerifyOtpAsync(string requestId, string prefix, string code, CancellationToken ct = default);
    Task<HubtelOtpSendResponse?> ResendOtpAsync(string requestId, CancellationToken ct = default);
}
