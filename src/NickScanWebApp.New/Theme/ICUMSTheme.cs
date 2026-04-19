using MudBlazor;

namespace NickScanWebApp.New.Theme
{
    /// <summary>
    /// Custom MudBlazor theme for NSCIS - ICUMS System
    /// Light Mode: Domiex-inspired (light bg, dark sidebar, colorful stat cards)
    /// Dark Mode: Domiex dark (deep slate bg, dark sidebar, vibrant accents)
    /// </summary>
    public class ICUMSTheme : MudTheme
    {
        public ICUMSTheme()
        {
            // ═══ LIGHT MODE: Government Executive ═══
            PaletteLight = new PaletteLight
            {
                // Primary — blue accent
                Primary = "#2563EB",
                PrimaryContrastText = "#ffffff",
                PrimaryDarken = "#1D4ED8",
                PrimaryLighten = "#60A5FA",

                // Secondary — slate
                Secondary = "#334155",
                SecondaryContrastText = "#ffffff",
                SecondaryDarken = "#1E293B",
                SecondaryLighten = "#475569",

                // Tertiary
                Tertiary = "#0369A1",
                TertiaryContrastText = "#ffffff",

                // Semantic Colors
                Info = "#0369A1",
                InfoContrastText = "#ffffff",

                Success = "#16A34A",
                SuccessContrastText = "#ffffff",

                Warning = "#D97706",
                WarningContrastText = "#ffffff",

                Error = "#DC2626",
                ErrorContrastText = "#ffffff",

                Dark = "#0F172A",
                DarkContrastText = "#ffffff",

                // Text Colors
                TextPrimary = "#020617",
                TextSecondary = "#64748B",
                TextDisabled = "rgba(0, 0, 0, 0.38)",

                // Action Colors
                ActionDefault = "rgba(0, 0, 0, 0.54)",
                ActionDisabled = "rgba(0, 0, 0, 0.26)",
                ActionDisabledBackground = "rgba(0, 0, 0, 0.12)",

                // Background — light slate (Domiex style)
                Background = "#f1f5f9",
                Surface = "#ffffff",

                // Dividers
                Divider = "#E2E8F0",
                DividerLight = "#F1F5F9",

                // Table Colors
                TableLines = "#E2E8F0",
                TableStriped = "rgba(0, 0, 0, 0.02)",
                TableHover = "#F8FAFC",

                // Lines & Borders
                LinesDefault = "#E2E8F0",
                LinesInputs = "#CBD5E1",

                // AppBar — white with border (not heavy blue)
                AppbarBackground = "#ffffff",
                AppbarText = "#0F172A",

                // Drawer (Side Navigation) — dark sidebar (Domiex style, always dark)
                DrawerBackground = "#1b2537",
                DrawerText = "rgba(255, 255, 255, 0.55)",
                DrawerIcon = "rgba(255, 255, 255, 0.55)",

                // Overlay
                OverlayLight = "rgba(255, 255, 255, 0.4)",
                OverlayDark = "rgba(0, 0, 0, 0.4)",
            };

            // ═══ DARK MODE: Security Command ═══
            PaletteDark = new PaletteDark
            {
                // Primary — green accent (neon on dark)
                Primary = "#22C55E",
                PrimaryContrastText = "#020617",
                PrimaryDarken = "#16A34A",
                PrimaryLighten = "#4ADE80",

                // Secondary — blue accent
                Secondary = "#0369A1",
                SecondaryContrastText = "#ffffff",
                SecondaryDarken = "#0C4A6E",
                SecondaryLighten = "#38BDF8",

                // Tertiary
                Tertiary = "#60A5FA",
                TertiaryContrastText = "#020617",

                // Semantic Colors
                Info = "#38BDF8",
                InfoContrastText = "#020617",

                Success = "#4ADE80",
                SuccessContrastText = "#020617",

                Warning = "#FBBF24",
                WarningContrastText = "#020617",

                Error = "#EF4444",
                ErrorContrastText = "#ffffff",

                Dark = "#334155",
                DarkContrastText = "#ffffff",

                // Text Colors
                TextPrimary = "rgba(255, 255, 255, 0.87)",
                TextSecondary = "#94A3B8",
                TextDisabled = "rgba(255, 255, 255, 0.38)",

                // Action Colors
                ActionDefault = "rgba(255, 255, 255, 0.54)",
                ActionDisabled = "rgba(255, 255, 255, 0.26)",
                ActionDisabledBackground = "rgba(255, 255, 255, 0.12)",

                // Background — deep dark (Domiex dark)
                Background = "#0f1117",
                Surface = "#1a1d2e",

                // Dividers
                Divider = "#1E293B",
                DividerLight = "#334155",

                // Table Colors
                TableLines = "#1E293B",
                TableStriped = "rgba(255, 255, 255, 0.02)",
                TableHover = "rgba(255, 255, 255, 0.03)",

                // AppBar — dark slate
                AppbarBackground = "#0F172A",
                AppbarText = "rgba(255, 255, 255, 0.87)",

                // Drawer — dark sidebar (matches light mode, always dark)
                DrawerBackground = "#111827",
                DrawerText = "rgba(255, 255, 255, 0.50)",
                DrawerIcon = "rgba(255, 255, 255, 0.50)",

                // Overlay
                OverlayLight = "rgba(255, 255, 255, 0.1)",
                OverlayDark = "rgba(0, 0, 0, 0.6)",
            };

            // Typography — Fira Sans for professional look
            Typography = new Typography();
            Typography.Default.FontFamily = new[] { "Roboto", "Helvetica", "Arial", "sans-serif" };

            // Shadow Configuration
            Shadows = new Shadow();

            // Z-Index Configuration
            ZIndex = new ZIndex
            {
                Drawer = 1100,
                AppBar = 1200,
                Dialog = 1300,
                Popover = 1400,
                Snackbar = 1500,
                Tooltip = 1600
            };

            // Layout Properties
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "10px",
                DrawerWidthLeft = "250px",
                DrawerWidthRight = "300px",
                DrawerMiniWidthLeft = "56px",
                DrawerMiniWidthRight = "56px",
                AppbarHeight = "52px"
            };
        }
    }
}
