namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// 6.07 (Sprint 4 — frontend contract de-fragmentation): direct interface for
    /// JWT token retrieval, used by ApiService instead of reflection-based lookup
    /// on AuthenticationStateProvider.
    ///
    /// The previous reflection probe (`GetType().GetMethod("GetTokenAsync", ...)`)
    /// fails silently if the method is renamed, the signature changes, or DI
    /// registers a different provider, leaving the request unauthenticated. With a
    /// typed interface, the failure surfaces at startup as a DI resolution error.
    ///
    /// Implementations: SimpleAuthStateProvider (NickScanWebApp.New.Services).
    /// </summary>
    public interface IAuthTokenSource
    {
        /// <summary>
        /// Returns the current JWT bearer token, or null if the user is not
        /// authenticated / the token cannot be retrieved.
        /// </summary>
        Task<string?> GetTokenAsync();
    }
}
