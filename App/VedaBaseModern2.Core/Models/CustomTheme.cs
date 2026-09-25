using System;
using System.Collections.Generic;

namespace VedaBaseModern.Core.Models
{
    /// <summary>
    /// Configuration data contract for the custom theme designer.
    /// Persisted to %LOCALAPPDATA%\VedaBaseModern\custom_theme.json.
    /// </summary>
    public class CustomTheme
    {
        public string Name { get; set; } = "Custom";
        public string BaseTheme { get; set; } = "Dark"; // "Dark" or "Light"

        // Core component colors (Hex strings #RRGGBB or #AARRGGBB)
        public string PageBackground { get; set; } = "#0F281E";
        public string CardBackground { get; set; } = "#16382A";
        public string NavigationPaneBackground { get; set; } = "#0A1C15";
        public string PrimaryText { get; set; } = "#F2F4F3";
        public string SecondaryText { get; set; } = "#A8BDB4";
        public string AccentColor { get; set; } = "#D4AF37";
        public string DividerColor { get; set; } = "#234D3C";

        // Highlight tint colors
        public string HighlightYellow { get; set; } = "#4DE8C547";
        public string HighlightGreen { get; set; } = "#4D5FA870";
        public string HighlightBlue { get; set; } = "#4D5B93C4";

        public CustomTheme Clone()
        {
            return new CustomTheme
            {
                Name = this.Name,
                BaseTheme = this.BaseTheme,
                PageBackground = this.PageBackground,
                CardBackground = this.CardBackground,
                NavigationPaneBackground = this.NavigationPaneBackground,
                PrimaryText = this.PrimaryText,
                SecondaryText = this.SecondaryText,
                AccentColor = this.AccentColor,
                DividerColor = this.DividerColor,
                HighlightYellow = this.HighlightYellow,
                HighlightGreen = this.HighlightGreen,
                HighlightBlue = this.HighlightBlue
            };
        }

        public static CustomTheme CreateDarkDefault() => new()
        {
            Name = "Temple Saffron (Dark)",
            BaseTheme = "Dark",
            PageBackground = "#0F281E",
            CardBackground = "#16382A",
            NavigationPaneBackground = "#0A1C15",
            PrimaryText = "#F2F4F3",
            SecondaryText = "#A8BDB4",
            AccentColor = "#D4AF37",
            DividerColor = "#234D3C",
            HighlightYellow = "#4DE8C547",
            HighlightGreen = "#4D5FA870",
            HighlightBlue = "#4D5B93C4"
        };

        public static CustomTheme CreateLightDefault() => new()
        {
            Name = "Vedic Parchment (Light)",
            BaseTheme = "Light",
            PageBackground = "#F3D4A5",
            CardBackground = "#F7DCAF",
            NavigationPaneBackground = "#EFCC99",
            PrimaryText = "#111111",
            SecondaryText = "#3A3024",
            AccentColor = "#9B6818",
            DividerColor = "#DFBD86",
            HighlightYellow = "#4DE8C547",
            HighlightGreen = "#4D5FA870",
            HighlightBlue = "#4D5B93C4"
        };

        public static Dictionary<string, CustomTheme> GetCuratedPresets() => new()
        {
            ["Temple Saffron"] = CreateDarkDefault(),
            ["Vrindavan Forest"] = new CustomTheme
            {
                Name = "Vrindavan Forest",
                BaseTheme = "Dark",
                PageBackground = "#0B2215",
                CardBackground = "#143823",
                NavigationPaneBackground = "#07170E",
                PrimaryText = "#F0F5F1",
                SecondaryText = "#9DBEA8",
                AccentColor = "#E5B869",
                DividerColor = "#1D4A30"
            },
            ["Yamuna Midnight"] = new CustomTheme
            {
                Name = "Yamuna Midnight",
                BaseTheme = "Dark",
                PageBackground = "#0B132B",
                CardBackground = "#1C2541",
                NavigationPaneBackground = "#070D1F",
                PrimaryText = "#FFFFFF",
                SecondaryText = "#8FA3BF",
                AccentColor = "#48CAE4",
                DividerColor = "#2E3C66"
            },
            ["Ancient Manuscript"] = new CustomTheme
            {
                Name = "Ancient Manuscript",
                BaseTheme = "Light",
                PageBackground = "#F8F4E8",
                CardBackground = "#EFE8D6",
                NavigationPaneBackground = "#E7DEC7",
                PrimaryText = "#2B1E16",
                SecondaryText = "#6B584B",
                AccentColor = "#C05621",
                DividerColor = "#D6C7AC"
            },
            ["Minimal Charcoal"] = new CustomTheme
            {
                Name = "Minimal Charcoal",
                BaseTheme = "Dark",
                PageBackground = "#121214",
                CardBackground = "#1E1E22",
                NavigationPaneBackground = "#0A0A0C",
                PrimaryText = "#F5F5F7",
                SecondaryText = "#9E9EA7",
                AccentColor = "#FF9F1C",
                DividerColor = "#2E2E35"
            },
            ["Lotus Rose"] = new CustomTheme
            {
                Name = "Lotus Rose",
                BaseTheme = "Dark",
                PageBackground = "#21151D",
                CardBackground = "#301F2B",
                NavigationPaneBackground = "#180E15",
                PrimaryText = "#F9F2F7",
                SecondaryText = "#C9ADC2",
                AccentColor = "#F28482",
                DividerColor = "#4B3243"
            },
            ["Sepia Warmth"] = new CustomTheme
            {
                Name = "Sepia Warmth",
                BaseTheme = "Light",
                PageBackground = "#FBF0D9",
                CardBackground = "#F4E3C1",
                NavigationPaneBackground = "#EBD4AB",
                PrimaryText = "#2C2216",
                SecondaryText = "#6E5D46",
                AccentColor = "#B85D19",
                DividerColor = "#DEC298"
            },
            ["Nordic Frost"] = new CustomTheme
            {
                Name = "Nordic Frost",
                BaseTheme = "Dark",
                PageBackground = "#1E222A",
                CardBackground = "#282C34",
                NavigationPaneBackground = "#181B21",
                PrimaryText = "#E6EDF3",
                SecondaryText = "#8B949E",
                AccentColor = "#58A6FF",
                DividerColor = "#30363D"
            },
            ["Solarized Earth"] = new CustomTheme
            {
                Name = "Solarized Earth",
                BaseTheme = "Dark",
                PageBackground = "#002B36",
                CardBackground = "#073642",
                NavigationPaneBackground = "#00212B",
                PrimaryText = "#EEE8D5",
                SecondaryText = "#93A1A1",
                AccentColor = "#B58900",
                DividerColor = "#0A4352"
            },
            ["Warm Sandstone"] = new CustomTheme
            {
                Name = "Warm Sandstone",
                BaseTheme = "Light",
                PageBackground = "#F4EFEB",
                CardBackground = "#EAE2D9",
                NavigationPaneBackground = "#DFD5C9",
                PrimaryText = "#23201D",
                SecondaryText = "#615951",
                AccentColor = "#A0522D",
                DividerColor = "#D0C3B5"
            },
            ["Obsidian Night"] = new CustomTheme
            {
                Name = "Obsidian Night",
                BaseTheme = "Dark",
                PageBackground = "#000000",
                CardBackground = "#121212",
                NavigationPaneBackground = "#080808",
                PrimaryText = "#E0E0E0",
                SecondaryText = "#888888",
                AccentColor = "#E5A93C",
                DividerColor = "#242424"
            }
        };
    }
}
