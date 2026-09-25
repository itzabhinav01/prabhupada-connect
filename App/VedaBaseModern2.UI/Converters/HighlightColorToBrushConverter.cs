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
            HighlightColor hc = value is HighlightColor color ? color : HighlightColor.Yellow;
            string key = hc switch
            {
                HighlightColor.Yellow => "HighlightYellowBrush",
                HighlightColor.Green => "HighlightGreenBrush",
                HighlightColor.Blue => "HighlightBlueBrush",
                _ => "HighlightYellowBrush"
            };
            if (Application.Current?.Resources.TryGetValue(key, out var brush) == true && brush is Brush b)
            {
                return b;
            }

            bool isDark = CustomThemeService.ActiveCustomTheme is { } custom
                ? !string.Equals(custom.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase)
                : Application.Current?.RequestedTheme == ApplicationTheme.Dark;

            return new SolidColorBrush(CustomThemeService.GetHighlightColor(hc, isDark));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
