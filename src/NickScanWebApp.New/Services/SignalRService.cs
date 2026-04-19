using Microsoft.AspNetCore.SignalR.Client;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Service for managing SignalR connection and real-time updates
    /// </summary>
    public class SignalRService : IAsyncDisposable
    {
        private HubConnection? _hubConnection;
        private readonly string _hubUrl;

        public event Action<string, string, DateTime>? OnNewScan;
        public event Action<string, bool>? OnICUMSUpdate;
        public event Action<string, string, string>? OnProcessingComplete;
        public event Action<string, string, string>? OnSystemAlert;

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public SignalRService(IConfiguration configuration)
        {
            var apiUrl = configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
            _hubUrl = $"{apiUrl}/hubs/scanner";
        }

        /// <summary>
        /// Start SignalR connection
        /// </summary>
        public async Task StartAsync()
        {
            if (_hubConnection != null)
                return;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Register event handlers
            _hubConnection.On<object>("NewScanReceived", (data) =>
            {
                // Parse and trigger event
                OnNewScan?.Invoke("", "", DateTime.Now);
            });

            _hubConnection.On<object>("ICUMSDataUpdated", (data) =>
            {
                OnICUMSUpdate?.Invoke("", true);
            });

            _hubConnection.On<object>("ProcessingCompleted", (data) =>
            {
                OnProcessingComplete?.Invoke("", "", "");
            });

            _hubConnection.On<object>("SystemAlert", (data) =>
            {
                OnSystemAlert?.Invoke("", "", "");
            });

            try
            {
                await _hubConnection.StartAsync();
            }
            catch (Exception)
            {
                // Silently fail if SignalR not available
            }
        }

        /// <summary>
        /// Stop SignalR connection
        /// </summary>
        public async Task StopAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }
    }
}

