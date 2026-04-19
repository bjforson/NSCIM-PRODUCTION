using MudBlazor;

namespace NickScanWebApp.Mobile.Theme
{
    /// <summary>
    /// Custom MudBlazor theme for Ghana Customs - ICUMS System
    /// Implements Ghana Customs branding with professional color palette
    /// </summary>
    public class ICUMSTheme : MudTheme
    {
        public ICUMSTheme()
        {
            // Light Mode Palette (Default)
            PaletteLight = new PaletteLight
            {
                // Primary Colors - Ghana Customs Official Colors
                Primary = "#1a237e",        // Deep Blue - Main brand color
                PrimaryContrastText = "#ffffff",
                PrimaryDarken = "#0d1642",
                PrimaryLighten = "#3949ab",
                
                // Secondary Colors
                Secondary = "#283593",      // Indigo - Secondary brand
                SecondaryContrastText = "#ffffff",
                SecondaryDarken = "#1a237e",
                SecondaryLighten = "#3f51b5",
                
                // Tertiary
                Tertiary = "#304ffe",       // Bright Blue
                TertiaryContrastText = "#ffffff",
                
                // Semantic Colors
                Info = "#0288d1",           // Information Blue
                InfoContrastText = "#ffffff",
                
                Success = "#2e7d32",        // Success Green
                SuccessContrastText = "#ffffff",
                
                Warning = "#ed6c02",        // Warning Orange
                WarningContrastText = "#ffffff",
                
                Error = "#d32f2f",          // Error Red
                ErrorContrastText = "#ffffff",
                
                Dark = "#212121",           // Dark Gray
                DarkContrastText = "#ffffff",
                
                // Text Colors
                TextPrimary = "rgba(0, 0, 0, 0.87)",
                TextSecondary = "rgba(0, 0, 0, 0.6)",
                TextDisabled = "rgba(0, 0, 0, 0.38)",
                
                // Action Colors
                ActionDefault = "rgba(0, 0, 0, 0.54)",
                ActionDisabled = "rgba(0, 0, 0, 0.26)",
                ActionDisabledBackground = "rgba(0, 0, 0, 0.12)",
                
                // Background Colors
                Background = "#f5f5f5",              // Light Gray background
                Surface = "#ffffff",                 // White surface
                
                // Dividers
                Divider = "rgba(0, 0, 0, 0.12)",
                DividerLight = "rgba(0, 0, 0, 0.06)",
                
                // Table Colors
                TableLines = "rgba(0, 0, 0, 0.12)",
                TableStriped = "rgba(0, 0, 0, 0.02)",
                TableHover = "rgba(0, 0, 0, 0.04)",
                
                // Lines & Borders
                LinesDefault = "rgba(0, 0, 0, 0.12)",
                LinesInputs = "rgba(0, 0, 0, 0.42)",
                
                // AppBar (Top Navigation)
                AppbarBackground = "#1a237e",        // Ghana Customs Blue
                AppbarText = "#ffffff",
                
                // Drawer (Side Navigation)
                DrawerBackground = "#ffffff",
                DrawerText = "rgba(0, 0, 0, 0.87)",
                DrawerIcon = "rgba(0, 0, 0, 0.54)",
                
                // Overlay
                OverlayLight = "rgba(255, 255, 255, 0.4)",
                OverlayDark = "rgba(0, 0, 0, 0.4)",
                
                // Ghana Flag Colors (Optional accents)
                // These can be used for special indicators or badges
                // Red = "#CE1126",
                // Gold = "#FCD116",
                // Green = "#006B3F"
            };
            
            // Dark Mode Palette
            PaletteDark = new PaletteDark
            {
                // Primary Colors
                Primary = "#5c6bc0",
                PrimaryContrastText = "#ffffff",
                PrimaryDarken = "#3f51b5",
                PrimaryLighten = "#7986cb",
                
                // Secondary Colors
                Secondary = "#7e57c2",
                SecondaryContrastText = "#ffffff",
                SecondaryDarken = "#673ab7",
                SecondaryLighten = "#9575cd",
                
                // Tertiary
                Tertiary = "#536dfe",
                TertiaryContrastText = "#ffffff",
                
                // Semantic Colors
                Info = "#29b6f6",
                InfoContrastText = "#000000",
                
                Success = "#66bb6a",
                SuccessContrastText = "#000000",
                
                Warning = "#ffa726",
                WarningContrastText = "#000000",
                
                Error = "#ef5350",
                ErrorContrastText = "#ffffff",
                
                Dark = "#424242",
                DarkContrastText = "#ffffff",
                
                // Text Colors
                TextPrimary = "rgba(255, 255, 255, 0.87)",
                TextSecondary = "rgba(255, 255, 255, 0.6)",
                TextDisabled = "rgba(255, 255, 255, 0.38)",
                
                // Action Colors
                ActionDefault = "rgba(255, 255, 255, 0.54)",
                ActionDisabled = "rgba(255, 255, 255, 0.26)",
                ActionDisabledBackground = "rgba(255, 255, 255, 0.12)",
                
                // Background Colors
                Background = "#121212",              // Dark background
                Surface = "#1e1e1e",                 // Surface color
                
                // Dividers
                Divider = "rgba(255, 255, 255, 0.12)",
                DividerLight = "rgba(255, 255, 255, 0.06)",
                
                // Table Colors
                TableLines = "rgba(255, 255, 255, 0.12)",
                TableStriped = "rgba(255, 255, 255, 0.02)",
                TableHover = "rgba(255, 255, 255, 0.04)",
                
                // AppBar
                AppbarBackground = "#1e1e1e",
                AppbarText = "rgba(255, 255, 255, 0.87)",
                
                // Drawer
                DrawerBackground = "#1e1e1e",
                DrawerText = "rgba(255, 255, 255, 0.87)",
                DrawerIcon = "rgba(255, 255, 255, 0.54)",
                
                // Overlay
                OverlayLight = "rgba(255, 255, 255, 0.2)",
                OverlayDark = "rgba(0, 0, 0, 0.6)",
            };
            
            // Typography Configuration - Simplified for MudBlazor 8.x
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
                DefaultBorderRadius = "4px",
                DrawerWidthLeft = "260px",
                DrawerWidthRight = "300px",
                DrawerMiniWidthLeft = "56px",
                DrawerMiniWidthRight = "56px",
                AppbarHeight = "64px"
            };
        }
    }
}

