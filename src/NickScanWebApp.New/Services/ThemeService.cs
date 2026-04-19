using Microsoft.JSInterop;

namespace NickScanWebApp.New.Services
{
    public class ThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        private bool _isDarkMode = false;
        private bool _initialized = false;

        public event Action? OnThemeChanged;

        public ThemeService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public bool IsDarkMode
        {
            get => _isDarkMode;
            private set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    OnThemeChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// Initialize theme from localStorage
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized) return;

            try
            {
                var savedTheme = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "theme-preference");
                IsDarkMode = savedTheme == "dark";
                _initialized = true;
            }
            catch
            {
                // If localStorage fails, use default (light mode)
                IsDarkMode = false;
                _initialized = true;
            }
        }

        /// <summary>
        /// Toggle between light and dark mode
        /// </summary>
        public async Task ToggleThemeAsync()
        {
            IsDarkMode = !IsDarkMode;
            await SaveThemePreferenceAsync();
        }

        /// <summary>
        /// Set theme explicitly
        /// </summary>
        public async Task SetThemeAsync(bool darkMode)
        {
            IsDarkMode = darkMode;
            await SaveThemePreferenceAsync();
        }

        /// <summary>
        /// Save theme preference to localStorage
        /// </summary>
        private async Task SaveThemePreferenceAsync()
        {
            try
            {
                var theme = IsDarkMode ? "dark" : "light";
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "theme-preference", theme);
            }
            catch
            {
                // Silently fail if localStorage is not available
            }
        }
    }
}

