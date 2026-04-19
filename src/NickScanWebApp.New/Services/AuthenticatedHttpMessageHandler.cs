namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// HTTP Message Handler that automatically adds JWT token to outgoing API requests
    /// </summary>
    public class AuthenticatedHttpMessageHandler : DelegatingHandler
    {
        private readonly SimpleAuthStateProvider _authProvider;
        private readonly ILogger<AuthenticatedHttpMessageHandler> _logger;

        public AuthenticatedHttpMessageHandler(
            SimpleAuthStateProvider authProvider,
            ILogger<AuthenticatedHttpMessageHandler> logger)
        {
            _authProvider = authProvider;
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                // Get the JWT token from auth provider
                var token = await _authProvider.GetTokenAsync();

                if (!string.IsNullOrEmpty(token))
                {
                    // Add Authorization header with Bearer token
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                    _logger.LogDebug("✅ Added JWT token to request: {Method} {Uri}",
                        request.Method, request.RequestUri);
                }
                else
                {
                    // Only log as debug for public endpoints or when user is not authenticated
                    // This reduces log noise for expected unauthenticated requests
                    _logger.LogDebug("⚠️ No auth token available for request: {Method} {Uri} - request will be unauthenticated",
                        request.Method, request.RequestUri);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving auth token from provider");
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}

