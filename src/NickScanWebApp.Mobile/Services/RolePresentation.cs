using MudBlazor;

namespace NickScanWebApp.Mobile.Services
{
    public static class RolePresentation
    {
        private record RoleVisual(Color ChipColor, Color AvatarColor);

        private static readonly IReadOnlyDictionary<string, RoleVisual> Visuals =
            new Dictionary<string, RoleVisual>(StringComparer.OrdinalIgnoreCase)
            {
                ["SuperAdmin"] = new RoleVisual(Color.Error, Color.Error),
                ["Super Administrator"] = new RoleVisual(Color.Error, Color.Error),
                ["Admin"] = new RoleVisual(Color.Warning, Color.Warning),
                ["Administrator"] = new RoleVisual(Color.Warning, Color.Warning),
                ["Lead"] = new RoleVisual(Color.Secondary, Color.Secondary),
                ["Manager"] = new RoleVisual(Color.Info, Color.Info),
                ["Supervisor"] = new RoleVisual(Color.Primary, Color.Primary),
                ["ScannerOperator"] = new RoleVisual(Color.Info, Color.Info),
                ["Scanner Operator"] = new RoleVisual(Color.Info, Color.Info),
                ["Operator"] = new RoleVisual(Color.Primary, Color.Primary),
                ["Analyst"] = new RoleVisual(Color.Info, Color.Info),
                ["Audit"] = new RoleVisual(Color.Info, Color.Info),
                ["CustomsOfficer"] = new RoleVisual(Color.Primary, Color.Primary),
                ["Customs Officer"] = new RoleVisual(Color.Primary, Color.Primary),
                ["Viewer"] = new RoleVisual(Color.Default, Color.Default)
            };

        public static Color GetChipColor(string? roleName)
        {
            if (roleName != null && Visuals.TryGetValue(roleName, out var visual))
            {
                return visual.ChipColor;
            }
            return Color.Default;
        }

        public static Color GetAvatarColor(string? roleName)
        {
            if (roleName != null && Visuals.TryGetValue(roleName, out var visual))
            {
                return visual.AvatarColor;
            }
            return Color.Default;
        }
    }
}

