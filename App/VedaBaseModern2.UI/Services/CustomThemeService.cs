using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VedaBaseModern.Core.Models;
using Windows.UI;

namespace VedaBaseModern.UI.Services
{
    /// <summary>
    /// Manages loading, saving, and applying user-defined custom themes.
    /// Persisted at %LOCALAPPDATA%\VedaBaseModern\custom_theme.json to avoid
    /// altering user.db schema or triggering SQLite migrations.
    /// </summary>
    public class CustomThemeService
    {
        private readonly string _configFilePath;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static CustomTheme? ActiveCustomTheme { get; set; }

        private static readonly string[] OverriddenResourceKeys = new[]
        {
            "ApplicationPageBackgroundThemeBrush",
            "NavigationViewDefaultPaneBackground",
            "NavigationViewExpandedPaneBackground",
            "NavigationViewTopPaneBackground",
            "NavigationViewContentBackground",
            "CardBackgroundFillColorDefaultBrush",
            "CardBackgroundFillColorSecondaryBrush",
            "LayerFillColorDefaultBrush",
            "AccentFillColorDefaultBrush",
            "AccentFillColorSecondaryBrush",
            "AccentFillColorTertiaryBrush",
            "TextControlHeaderForeground",
            "TextFillColorPrimaryBrush",
            "TextFillColorSecondaryBrush",
            "TextFillColorTertiaryBrush",
            "DividerStrokeColorDefaultBrush",
            "CardStrokeColorDefaultBrush",
            "HighlightYellowBrush",
            "HighlightGreenBrush",
            "HighlightBlueBrush"
        };

        public CustomThemeService(string? configFilePath = null)
        {
            if (configFilePath != null)
            {
                _configFilePath = configFilePath;
            }
            else
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VedaBaseModern");
                Directory.CreateDirectory(dir);
                _configFilePath = Path.Combine(dir, "custom_theme.json");
            }
        }

        public async Task<CustomTheme> LoadThemeAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = await File.ReadAllTextAsync(_configFilePath);
                    var theme = JsonSerializer.Deserialize<CustomTheme>(json);
                    if (theme != null) return theme;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CustomThemeService] Failed to load custom theme: {ex.Message}");
            }

            return CustomTheme.CreateDarkDefault();
        }

        public async Task SaveThemeAsync(CustomTheme theme)
        {
            try
            {
                string dir = Path.GetDirectoryName(_configFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(theme, JsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CustomThemeService] Failed to save custom theme: {ex.Message}");
            }
        }

        public void ApplyCustomTheme(CustomTheme theme, Window? window)
        {
            try
            {
                ActiveCustomTheme = theme;
                window ??= VedaBaseModern_UI.App.Current?.MainWindowInstance;
                var brushes = BuildBrushDictionary(theme);

                // 1. Inject into Application.Current.Resources flat AND all its ThemeDictionaries
                if (Application.Current != null)
                {
                    foreach (var (key, brush) in brushes)
                    {
                        try
                        {
                            Application.Current.Resources[key] = new SolidColorBrush(brush.Color);
                        }
                        catch { }
                    }
                    InjectIntoApplicationThemeDictionaries(brushes);
                }

                // 2. Inject into Window RootGrid resources (both flat and ThemeDictionaries)
                if (window?.Content is FrameworkElement root)
                {
                    foreach (var (key, brush) in brushes)
                    {
                        try
                        {
                            root.Resources[key] = new SolidColorBrush(brush.Color);
                        }
                        catch { }
                    }

                    ResourceDictionary CreateThemeDict()
                    {
                        var d = new ResourceDictionary();
                        foreach (var (key, brush) in brushes)
                        {
                            try
                            {
                                d[key] = new SolidColorBrush(brush.Color);
                            }
                            catch { }
                        }
                        return d;
                    }

                    try
                    {
                        root.Resources.ThemeDictionaries["Default"] = CreateThemeDict();
                        root.Resources.ThemeDictionaries["Dark"] = CreateThemeDict();
                        root.Resources.ThemeDictionaries["Light"] = CreateThemeDict();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CustomThemeService] root ThemeDictionaries: {ex.Message}");
                    }

                    var targetTheme = string.Equals(theme.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase)
                        ? ElementTheme.Light
                        : ElementTheme.Dark;

                    // WinUI 3 ThemeResource flush: toggle theme to force resource re-evaluation down the tree
                    var nudgeTheme = targetTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                    root.RequestedTheme = nudgeTheme;
                    root.RequestedTheme = targetTheme;
                }

                // 3. Update MainWindow root visual elements directly
                if (window is VedaBaseModern_UI.MainWindow mw)
                {
                    mw.ApplyCustomWindowChrome(
                        new SolidColorBrush(brushes["ApplicationPageBackgroundThemeBrush"].Color),
                        new SolidColorBrush(brushes["TextFillColorPrimaryBrush"].Color));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CustomThemeService] Failed to apply custom theme: {ex.Message}");
            }
        }

        public void RemoveCustomTheme(ElementTheme targetTheme, Window? window)
        {
            try
            {
                ActiveCustomTheme = null;
                window ??= VedaBaseModern_UI.App.Current?.MainWindowInstance;

                if (Application.Current != null)
                {
                    foreach (var key in OverriddenResourceKeys)
                    {
                        try
                        {
                            Application.Current.Resources.Remove(key);
                        }
                        catch { }
                    }
                    RestoreApplicationThemeDictionaries();
                }

                if (window?.Content is FrameworkElement root)
                {
                    foreach (var key in OverriddenResourceKeys)
                    {
                        try
                        {
                            root.Resources.Remove(key);
                        }
                        catch { }
                    }

                    try
                    {
                        root.Resources.ThemeDictionaries.Remove("Default");
                        root.Resources.ThemeDictionaries.Remove("Dark");
                        root.Resources.ThemeDictionaries.Remove("Light");
                    }
                    catch { }

                    var nudgeTheme = targetTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                    root.RequestedTheme = nudgeTheme;
                    root.RequestedTheme = targetTheme;
                }

                if (window is VedaBaseModern_UI.MainWindow mw)
                {
                    mw.RestoreWindowChrome();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CustomThemeService] Failed to remove custom theme: {ex.Message}");
            }
        }

        private static void InjectIntoApplicationThemeDictionaries(Dictionary<string, SolidColorBrush> brushes)
        {
            if (Application.Current == null) return;

            InjectIntoDictionaryGroup(Application.Current.Resources.ThemeDictionaries, brushes);

            foreach (var merged in Application.Current.Resources.MergedDictionaries)
            {
                if (merged is Microsoft.UI.Xaml.Controls.XamlControlsResources) continue;
                InjectIntoDictionaryGroup(merged.ThemeDictionaries, brushes);
            }
        }

        private static void InjectIntoDictionaryGroup(IDictionary<object, object>? themeDicts, Dictionary<string, SolidColorBrush> brushes)
        {
            if (themeDicts == null) return;

            foreach (var themeKey in new[] { "Default", "Dark", "Light" })
            {
                if (themeDicts.TryGetValue(themeKey, out var dictObj) && dictObj is ResourceDictionary dict)
                {
                    foreach (var (key, brush) in brushes)
                    {
                        try
                        {
                            dict[key] = new SolidColorBrush(brush.Color);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CustomThemeService] Could not set {key}: {ex.Message}");
                        }
                    }
                }
            }
        }

        private static void RestoreApplicationThemeDictionaries()
        {
            if (Application.Current == null) return;

            var darkBrushes = GetStandardDarkBrushes();
            var lightBrushes = GetStandardLightBrushes();

            RestoreDictionaryGroup(Application.Current.Resources.ThemeDictionaries, darkBrushes, lightBrushes);

            foreach (var merged in Application.Current.Resources.MergedDictionaries)
            {
                if (merged is Microsoft.UI.Xaml.Controls.XamlControlsResources) continue;
                RestoreDictionaryGroup(merged.ThemeDictionaries, darkBrushes, lightBrushes);
            }
        }

        private static void RestoreDictionaryGroup(IDictionary<object, object>? themeDicts, Dictionary<string, SolidColorBrush> darkBrushes, Dictionary<string, SolidColorBrush> lightBrushes)
        {
            if (themeDicts == null) return;

            foreach (var themeKey in new[] { "Default", "Dark" })
            {
                if (themeDicts.TryGetValue(themeKey, out var dictObj) && dictObj is ResourceDictionary dict)
                {
                    foreach (var (key, brush) in darkBrushes)
                    {
                        try
                        {
                            dict[key] = new SolidColorBrush(brush.Color);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CustomThemeService] Could not restore {key}: {ex.Message}");
                        }
                    }
                }
            }

            if (themeDicts.TryGetValue("Light", out var lightObj) && lightObj is ResourceDictionary lightDict)
            {
                foreach (var (key, brush) in lightBrushes)
                {
                    try
                    {
                        lightDict[key] = new SolidColorBrush(brush.Color);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CustomThemeService] Could not restore light {key}: {ex.Message}");
                    }
                }
            }
        }

        private static Dictionary<string, SolidColorBrush> GetStandardDarkBrushes() => new()
        {
            ["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x0F, 0x28, 0x1E)),
            ["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x1C, 0x15)),
            ["NavigationViewExpandedPaneBackground"] = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x1C, 0x15)),
            ["NavigationViewTopPaneBackground"] = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x1C, 0x15)),
            ["NavigationViewContentBackground"] = new SolidColorBrush(Color.FromArgb(255, 0x0F, 0x28, 0x1E)),
            ["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x16, 0x38, 0x2A)),
            ["CardBackgroundFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x13, 0x30, 0x24)),
            ["LayerFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x16, 0x38, 0x2A)),
            ["AccentFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xD4, 0xAF, 0x37)),
            ["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xC5, 0xA0, 0x59)),
            ["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xB0, 0x8D, 0x45)),
            ["TextControlHeaderForeground"] = new SolidColorBrush(Color.FromArgb(255, 0xD4, 0xAF, 0x37)),
            ["TextFillColorPrimaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xF2, 0xF4, 0xF3)),
            ["TextFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xA8, 0xBD, 0xB4)),
            ["TextFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xF2, 0xF4, 0xF3)),
            ["DividerStrokeColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x23, 0x4D, 0x3C)),
            ["CardStrokeColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x23, 0x4D, 0x3C)),
            ["HighlightYellowBrush"] = new SolidColorBrush(Color.FromArgb(0x4D, 0xE8, 0xC5, 0x47)),
            ["HighlightGreenBrush"] = new SolidColorBrush(Color.FromArgb(0x4D, 0x5F, 0xA8, 0x70)),
            ["HighlightBlueBrush"] = new SolidColorBrush(Color.FromArgb(0x4D, 0x5B, 0x93, 0xC4))
        };

        private static Dictionary<string, SolidColorBrush> GetStandardLightBrushes() => new()
        {
            ["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xF3, 0xD4, 0xA5)),
            ["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(Color.FromArgb(255, 0xEF, 0xCC, 0x99)),
            ["NavigationViewExpandedPaneBackground"] = new SolidColorBrush(Color.FromArgb(255, 0xEF, 0xCC, 0x99)),
            ["NavigationViewTopPaneBackground"] = new SolidColorBrush(Color.FromArgb(255, 0xEF, 0xCC, 0x99)),
            ["NavigationViewContentBackground"] = new SolidColorBrush(Color.FromArgb(255, 0xF3, 0xD4, 0xA5)),
            ["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xF7, 0xDC, 0xAF)),
            ["CardBackgroundFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xF5, 0xD8, 0xA9)),
            ["LayerFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xF7, 0xDC, 0xAF)),
            ["AccentFillColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x9B, 0x68, 0x18)),
            ["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x8C, 0x5A, 0x13)),
            ["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x7A, 0x4C, 0x0D)),
            ["TextControlHeaderForeground"] = new SolidColorBrush(Color.FromArgb(255, 0x9B, 0x68, 0x18)),
            ["TextFillColorPrimaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x11, 0x11, 0x11)),
            ["TextFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x3A, 0x30, 0x24)),
            ["TextFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromArgb(255, 0x17, 0x12, 0x0D)),
            ["DividerStrokeColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xDF, 0xBD, 0x86)),
            ["CardStrokeColorDefaultBrush"] = new SolidColorBrush(Color.FromArgb(255, 0xDF, 0xBD, 0x86)),
            ["HighlightYellowBrush"] = new SolidColorBrush(Color.FromArgb(0x66, 0xE6, 0xA1, 0x22)),
            ["HighlightGreenBrush"] = new SolidColorBrush(Color.FromArgb(0x66, 0x6F, 0x9F, 0x7A)),
            ["HighlightBlueBrush"] = new SolidColorBrush(Color.FromArgb(0x66, 0x5B, 0x8F, 0xC9))
        };

        public Dictionary<string, SolidColorBrush> BuildBrushDictionary(CustomTheme theme)
        {
            var pageBg = ParseColor(theme.PageBackground, Color.FromArgb(255, 15, 40, 30));
            var cardBg = ParseColor(theme.CardBackground, Color.FromArgb(255, 22, 56, 42));
            var navBg = ParseColor(theme.NavigationPaneBackground, Color.FromArgb(255, 10, 28, 21));
            var primaryText = ParseColor(theme.PrimaryText, Color.FromArgb(255, 242, 244, 243));
            var secondaryText = ParseColor(theme.SecondaryText, Color.FromArgb(255, 168, 189, 180));
            var accent = ParseColor(theme.AccentColor, Color.FromArgb(255, 212, 175, 55));
            var divider = ParseColor(theme.DividerColor, Color.FromArgb(255, 35, 77, 60));

            var hlYellow = ParseColor(theme.HighlightYellow, Color.FromArgb(0x4D, 0xE8, 0xC5, 0x47));
            var hlGreen = ParseColor(theme.HighlightGreen, Color.FromArgb(0x4D, 0x5F, 0xA8, 0x70));
            var hlBlue = ParseColor(theme.HighlightBlue, Color.FromArgb(0x4D, 0x5B, 0x93, 0xC4));

            return new Dictionary<string, SolidColorBrush>
            {
                ["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(pageBg),
                ["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(navBg),
                ["NavigationViewExpandedPaneBackground"] = new SolidColorBrush(navBg),
                ["NavigationViewTopPaneBackground"] = new SolidColorBrush(navBg),
                ["NavigationViewContentBackground"] = new SolidColorBrush(pageBg),
                ["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(cardBg),
                ["CardBackgroundFillColorSecondaryBrush"] = new SolidColorBrush(cardBg),
                ["LayerFillColorDefaultBrush"] = new SolidColorBrush(cardBg),
                ["AccentFillColorDefaultBrush"] = new SolidColorBrush(accent),
                ["AccentFillColorSecondaryBrush"] = new SolidColorBrush(accent),
                ["AccentFillColorTertiaryBrush"] = new SolidColorBrush(accent),
                ["TextControlHeaderForeground"] = new SolidColorBrush(accent),
                ["TextFillColorPrimaryBrush"] = new SolidColorBrush(primaryText),
                ["TextFillColorSecondaryBrush"] = new SolidColorBrush(secondaryText),
                ["TextFillColorTertiaryBrush"] = new SolidColorBrush(secondaryText),
                ["DividerStrokeColorDefaultBrush"] = new SolidColorBrush(divider),
                ["CardStrokeColorDefaultBrush"] = new SolidColorBrush(divider),
                ["HighlightYellowBrush"] = new SolidColorBrush(hlYellow),
                ["HighlightGreenBrush"] = new SolidColorBrush(hlGreen),
                ["HighlightBlueBrush"] = new SolidColorBrush(hlBlue)
            };
        }

        public static Color GetPrimaryTextColor(bool isDark)
        {
            if (ActiveCustomTheme != null)
            {
                return ParseColor(ActiveCustomTheme.PrimaryText, isDark ? Color.FromArgb(255, 0xF2, 0xF4, 0xF3) : Color.FromArgb(255, 0x11, 0x11, 0x11));
            }
            return isDark ? Color.FromArgb(255, 0xF2, 0xF4, 0xF3) : Color.FromArgb(255, 0x11, 0x11, 0x11);
        }

        public static Color GetSecondaryTextColor(bool isDark)
        {
            if (ActiveCustomTheme != null)
            {
                return ParseColor(ActiveCustomTheme.SecondaryText, isDark ? Color.FromArgb(255, 0xA8, 0xBD, 0xB4) : Color.FromArgb(255, 0x3A, 0x30, 0x24));
            }
            return isDark ? Color.FromArgb(255, 0xA8, 0xBD, 0xB4) : Color.FromArgb(255, 0x3A, 0x30, 0x24);
        }

        public static Color GetAccentColor(bool isDark)
        {
            if (ActiveCustomTheme != null)
            {
                return ParseColor(ActiveCustomTheme.AccentColor, isDark ? Color.FromArgb(255, 0xE5, 0xA9, 0x3C) : Color.FromArgb(255, 0xA6, 0x3A, 0x2A));
            }
            return isDark ? Color.FromArgb(255, 0xE5, 0xA9, 0x3C) : Color.FromArgb(255, 0xA6, 0x3A, 0x2A);
        }

        private static List<HighlightColorItem>? _activeHighlightPalette;

        public static List<HighlightColorItem> ActiveHighlightPalette
        {
            get => _activeHighlightPalette ??= HighlightColorHelper.CreateDefaultPalette();
            set
            {
                _activeHighlightPalette = value;
                UpdateHighlightBrushes();
            }
        }

        public static void SetHighlightPalette(List<HighlightColorItem>? palette)
        {
            if (palette != null && palette.Count > 0)
            {
                _activeHighlightPalette = palette;
            }
            else
            {
                _activeHighlightPalette = HighlightColorHelper.CreateDefaultPalette();
            }
            UpdateHighlightBrushes();
        }

        public static void UpdateHighlightBrushes()
        {
            try
            {
                if (Application.Current?.Resources == null) return;
                bool isDark = ActiveCustomTheme is { } custom
                    ? !string.Equals(custom.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase)
                    : Application.Current.RequestedTheme == ApplicationTheme.Dark;

                var res = Application.Current.Resources;
                var c1 = GetHighlightColor(HighlightColor.Colour1, isDark);
                var c2 = GetHighlightColor(HighlightColor.Colour2, isDark);
                var c3 = GetHighlightColor(HighlightColor.Colour3, isDark);

                res["HighlightYellowBrush"] = new SolidColorBrush(c1);
                res["HighlightGreenBrush"] = new SolidColorBrush(c2);
                res["HighlightBlueBrush"] = new SolidColorBrush(c3);

                if (_activeHighlightPalette != null)
                {
                    foreach (var item in _activeHighlightPalette)
                    {
                        var col = ParseColor(item.HexColor, c1);
                        res[$"HighlightColour{item.Slot}Brush"] = new SolidColorBrush(col);
                    }
                }
            }
            catch { }
        }

        public static Color GetHighlightColor(HighlightColor color, bool isDark)
        {
            int slot = HighlightColorHelper.ToSlot(color);
            if (_activeHighlightPalette != null)
            {
                var match = _activeHighlightPalette.Find(p => p.Slot == slot);
                if (match != null && !string.IsNullOrWhiteSpace(match.HexColor))
                {
                    Color fallback = GetDefaultHighlightColor(slot, isDark);
                    return ParseColor(match.HexColor, fallback);
                }
            }

            if (ActiveCustomTheme != null)
            {
                string hex = color switch
                {
                    HighlightColor.Yellow => ActiveCustomTheme.HighlightYellow,
                    HighlightColor.Green => ActiveCustomTheme.HighlightGreen,
                    _ => ActiveCustomTheme.HighlightBlue
                };
                Color fallback = GetDefaultHighlightColor(slot, isDark);
                return ParseColor(hex, fallback);
            }

            return GetDefaultHighlightColor(slot, isDark);
        }

        private static Color GetDefaultHighlightColor(int slot, bool isDark) => slot switch
        {
            1 => isDark ? Color.FromArgb(255, 0xF0, 0xD8, 0x70) : Color.FromArgb(255, 0xE6, 0xA1, 0x22),
            2 => isDark ? Color.FromArgb(255, 0xA8, 0xD6, 0xB0) : Color.FromArgb(255, 0x6F, 0x9F, 0x7A),
            3 => isDark ? Color.FromArgb(255, 0xA6, 0xC8, 0xE8) : Color.FromArgb(255, 0x5B, 0x8F, 0xC9),
            4 => isDark ? Color.FromArgb(255, 0xEF, 0x9A, 0x9A) : Color.FromArgb(255, 0xE5, 0x73, 0x73),
            5 => isDark ? Color.FromArgb(255, 0xCE, 0x93, 0xD8) : Color.FromArgb(255, 0xBA, 0x68, 0xC8),
            6 => isDark ? Color.FromArgb(255, 0x80, 0xDE, 0xEA) : Color.FromArgb(255, 0x4D, 0xD0, 0xE1),
            _ => isDark ? Color.FromArgb(255, 0xF0, 0xD8, 0x70) : Color.FromArgb(255, 0xE6, 0xA1, 0x22)
        };

        public static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            string clean = hex.Trim().TrimStart('#');

            try
            {
                if (clean.Length == 6) // RRGGBB
                {
                    byte r = Convert.ToByte(clean[..2], 16);
                    byte g = Convert.ToByte(clean.Substring(2, 2), 16);
                    byte b = Convert.ToByte(clean.Substring(4, 2), 16);
                    return Color.FromArgb(255, r, g, b);
                }
                if (clean.Length == 8) // AARRGGBB
                {
                    byte a = Convert.ToByte(clean[..2], 16);
                    byte r = Convert.ToByte(clean.Substring(2, 2), 16);
                    byte g = Convert.ToByte(clean.Substring(4, 2), 16);
                    byte b = Convert.ToByte(clean.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
                if (clean.Length == 3) // RGB
                {
                    byte r = Convert.ToByte($"{clean[0]}{clean[0]}", 16);
                    byte g = Convert.ToByte($"{clean[1]}{clean[1]}", 16);
                    byte b = Convert.ToByte($"{clean[2]}{clean[2]}", 16);
                    return Color.FromArgb(255, r, g, b);
                }
                if (clean.Length == 4) // ARGB
                {
                    byte a = Convert.ToByte($"{clean[0]}{clean[0]}", 16);
                    byte r = Convert.ToByte($"{clean[1]}{clean[1]}", 16);
                    byte g = Convert.ToByte($"{clean[2]}{clean[2]}", 16);
                    byte b = Convert.ToByte($"{clean[3]}{clean[3]}", 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch
            {
                // Return fallback on parse failure
            }

            return fallback;
        }

        public static string ToHex(Color color, bool includeAlpha = true)
        {
            return includeAlpha
                ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>
        /// Synchronizes a ContentDialog's RequestedTheme, background, and brush resources
        /// with the application's active theme (Light, Dark, Sepia, Solarized, or Custom)
        /// so popups never appear mismatched against the app window.
        /// </summary>
        public static void SyncDialogTheme(ContentDialog? dialog, XamlRoot? xamlRoot)
        {
            if (dialog == null) return;

            // 1. Establish XamlRoot and synchronize RequestedTheme from visual tree
            if (xamlRoot != null)
            {
                dialog.XamlRoot = xamlRoot;
                if (xamlRoot.Content is FrameworkElement fe)
                {
                    dialog.RequestedTheme = fe.ActualTheme;
                }
            }
            else if (Application.Current is VedaBaseModern_UI.App app && app.MainWindowInstance?.Content is FrameworkElement rootFe)
            {
                dialog.XamlRoot = rootFe.XamlRoot;
                dialog.RequestedTheme = rootFe.ActualTheme;
            }

            // 2. Resolve default theme brushes from application resources safely
            if (Application.Current?.Resources != null)
            {
                if (Application.Current.Resources.TryGetValue("LayerFillColorDefaultBrush", out var bg) && bg is Brush bgBrush)
                {
                    dialog.Background = bgBrush;
                }
                if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke) && stroke is Brush strokeBrush)
                {
                    dialog.BorderBrush = strokeBrush;
                }
                if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var fg) && fg is Brush fgBrush)
                {
                    dialog.Foreground = fgBrush;
                }
            }

            // 3. If a user custom theme is active, inject full brush overrides into the dialog's resource scope
            if (ActiveCustomTheme != null)
            {
                var service = new CustomThemeService();
                var brushes = service.BuildBrushDictionary(ActiveCustomTheme);
                foreach (var (key, brush) in brushes)
                {
                    try
                    {
                        dialog.Resources[key] = new SolidColorBrush(brush.Color);
                    }
                    catch { }
                }

                if (brushes.TryGetValue("LayerFillColorDefaultBrush", out var customBg))
                {
                    dialog.Background = customBg;
                }
                if (brushes.TryGetValue("CardStrokeColorDefaultBrush", out var customStroke))
                {
                    dialog.BorderBrush = customStroke;
                }
                if (brushes.TryGetValue("TextFillColorPrimaryBrush", out var customFg))
                {
                    dialog.Foreground = customFg;
                }
            }
        }
    }
}
