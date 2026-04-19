using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Models;

namespace NickComms.Gateway.Services;

public class HubtelClient : IHubtelClient
{
    private readonly HttpClient _smsHttp;
    private readonly HttpClient _otpHttp;
    private readonly ILogger<HubtelClient> _logger;

    public HubtelClient(
        IHttpClientFactory httpFactory,
        IOptions<HubtelOptions> options,
        ILogger<HubtelClient> logger)
    {
        _logger = logger;
        var opts = options.Value;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));

        _smsHttp = httpFactory.CreateClient("HubtelSms");
        _smsHttp.BaseAddress = new Uri(opts.SmsBaseUrl.TrimEnd('/') + "/");
        _smsHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _smsHttp.Timeout = TimeSpan.FromSeconds(30);

        _otpHttp = httpFactory.CreateClient("HubtelOtp");
        _otpHttp.BaseAddress = new Uri(opts.OtpBaseUrl.TrimEnd('/') + "/");
        _otpHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _otpHttp.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<HubtelSendResponse?> SendSmsAsync(string from, string to, string content, CancellationToken ct = default)
    {
        var request = new HubtelSendRequest { From = from, To = to, Content = content };
        _logger.LogInformation("Sending SMS to {To} from {From}", to, from);

        var response = await _smsHttp.PostAsJsonAsync("v1/messages/send", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Hubtel SMS failed: {Status} {Body}", response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<HubtelSendResponse>(ct);
    }

    public async Task<HubtelStatusResponse?> GetSmsStatusAsync(string messageId, CancellationToken ct = default)
    {
        var response = await _smsHttp.GetAsync($"v1/messages/{messageId}", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Hubtel status check failed for {MessageId}: {Status}", messageId, response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<HubtelStatusResponse>(ct);
    }

    public async Task<HubtelOtpSendResponse?> SendOtpAsync(string senderId, string phoneNumber, string countryCode, CancellationToken ct = default)
    {
        var request = new HubtelOtpSendRequest
        {
            SenderId = senderId,
            PhoneNumber = phoneNumber,
            CountryCode = countryCode
        };

        _logger.LogInformation("Sending OTP to {Phone}", phoneNumber);
        var response = await _otpHttp.PostAsJsonAsync("otp/send", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Hubtel OTP send failed: {Status} {Body}", response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<HubtelOtpSendResponse>(ct);
    }

    public async Task<bool> VerifyOtpAsync(string requestId, string prefix, string code, CancellationToken ct = default)
    {
        var request = new HubtelOtpVerifyRequest
        {
            RequestId = requestId,
            Prefix = prefix,
            Code = code
        };

        _logger.LogInformation("Verifying OTP for requestId {RequestId}", requestId);
        var response = await _otpHttp.PostAsJsonAsync("otp/verify", request, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<HubtelOtpSendResponse?> ResendOtpAsync(string requestId, CancellationToken ct = default)
    {
        var request = new HubtelOtpResendRequest { RequestId = requestId };

        _logger.LogInformation("Resending OTP for requestId {RequestId}", requestId);
        var response = await _otpHttp.PostAsJsonAsync("otp/resend", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Hubtel OTP resend failed: {Status} {Body}", response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<HubtelOtpSendResponse>(ct);
    }
}
