using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using VedaBaseModern.Core.Models;

using VedaBaseModern.UI.Services;

namespace VedaBaseModern.UI.Converters
{
    /// <summary>
    /// Resolves a HighlightColor enum value to the theme's current
    /// HighlightYellow/Green/BlueBrush - looked up via
    /// Application.Current.Resources or CustomThemeService fallback.
    /// </summary>
    public class HighlightColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            HighlightColor hc = HighlightColor.Colour1;
            if (value is HighlightColor color) hc = color;
            else if (value is string s) hc = HighlightColorHelper.Parse(s);
            else if (value is int slot) hc = HighlightColorHelper.FromSlot(slot);

            bool isDark = CustomThemeService.ActiveCustomTheme is { } custom
                ? !string.Equals(custom.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase)
                : Application.Current?.RequestedTheme == ApplicationTheme.Dark;

            var col = CustomThemeService.GetHighlightColor(hc, isDark);
            if (parameter is string p && (p.Equals("semi", StringComparison.OrdinalIgnoreCase) || p.Equals("alpha", StringComparison.OrdinalIgnoreCase)))
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(102, col.R, col.G, col.B));
            }
            return new SolidColorBrush(col);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
