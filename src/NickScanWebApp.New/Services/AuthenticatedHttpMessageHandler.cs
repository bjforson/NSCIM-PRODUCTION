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
            var hasToken = false;

            try
            {
                // Get the JWT token from auth provider (session storage → in-memory cache)
                var token = await _authProvider.GetTokenAsync();

                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    hasToken = true;
                    _logger.LogDebug("✅ Added JWT token to request: {Method} {Uri}",
                        request.Method, request.RequestUri);
                }
                else
                {
                    // 2026-04-19: upgraded from Debug to Warning. The API used to quietly
                    // return fake zeros to unauthenticated callers, so this case was
                    // operationally invisible. Now it returns 401 — surfacing this in the
                    // WebApp logs lets ops see expired-session symptoms directly.
                    _logger.LogWarning("⚠️ No auth token available for API request: {Method} {Uri} — user session may have expired",
                        request.Method, request.RequestUri);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving auth token from provider");
            }

            var response = await base.SendAsync(request, cancellationToken);

            // 2026-04-19: surface 401s explicitly so expired sessions are visible.
            // Blazor circuits can hit 401 if the JWT expired during a long-lived page;
            // without this log we'd only see the downstream UI symptom.
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("🔒 API returned 401 for {Method} {Uri} (token attached: {HasToken}). User likely needs to re-authenticate.",
                    request.Method, request.RequestUri, hasToken);
            }

            return response;
        }
    }
}

