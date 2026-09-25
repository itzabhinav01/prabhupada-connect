using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.UI.Services
{
    public class ReadingPreferences
    {
        /// <summary>
        /// Text brightness percentage for dark mode reading (50% to 100%).
        /// 100% = Standard crisp white (#F2F4F3 / #FFFFFF).
        /// 85%  = Soft White (eliminates night glare).
        /// 70%  = Muted Grey (relaxing in dark rooms).
        /// 55%  = Subdued (ultra-low fatigue before sleep).
        /// </summary>
        public int TextBrightness { get; set; } = 100;
    }

    public class ReadingPreferencesService
    {
        private readonly string _configFilePath;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public ReadingPreferences ActivePreferences { get; private set; } = new();

        public event Action<int>? TextBrightnessChanged;

        public ReadingPreferencesService(string? configFilePath = null)
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
                _configFilePath = Path.Combine(dir, "reading_preferences.json");
            }
        }

        public async Task<ReadingPreferences> LoadPreferencesAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = await File.ReadAllTextAsync(_configFilePath);
                    var prefs = JsonSerializer.Deserialize<ReadingPreferences>(json);
                    if (prefs != null)
                    {
                        prefs.TextBrightness = Math.Clamp(prefs.TextBrightness, 50, 100);
                        ActivePreferences = prefs;
                        return prefs;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadingPreferencesService] Failed to load preferences: {ex.Message}");
            }

            ActivePreferences = new ReadingPreferences();
            return ActivePreferences;
        }

        public async Task SavePreferencesAsync(ReadingPreferences prefs)
        {
            try
            {
                prefs.TextBrightness = Math.Clamp(prefs.TextBrightness, 50, 100);
                ActivePreferences = prefs;

                string dir = Path.GetDirectoryName(_configFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(prefs, JsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
                TextBrightnessChanged?.Invoke(prefs.TextBrightness);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadingPreferencesService] Failed to save preferences: {ex.Message}");
            }
        }

        public async Task SetTextBrightnessAsync(int brightness)
        {
            ActivePreferences.TextBrightness = Math.Clamp(brightness, 50, 100);
            await SavePreferencesAsync(ActivePreferences);
        }

        public static Color DimColor(Color baseColor, int brightnessPercentage)
        {
            if (brightnessPercentage >= 100) return baseColor;
            double factor = Math.Clamp(brightnessPercentage, 40, 100) / 100.0;
            byte r = (byte)Math.Clamp(Math.Round(baseColor.R * factor), 0, 255);
            byte g = (byte)Math.Clamp(Math.Round(baseColor.G * factor), 0, 255);
            byte b = (byte)Math.Clamp(Math.Round(baseColor.B * factor), 0, 255);
            return Color.FromArgb(baseColor.A, r, g, b);
        }

        public Color GetPrimaryReadingTextColor(bool isDark)
        {
            Color baseColor = CustomThemeService.GetPrimaryTextColor(isDark);
            if (!isDark) return baseColor;
            return DimColor(baseColor, ActivePreferences.TextBrightness);
        }

        public Color GetSecondaryReadingTextColor(bool isDark)
        {
            Color baseColor = CustomThemeService.GetSecondaryTextColor(isDark);
            if (!isDark) return baseColor;
            return DimColor(baseColor, ActivePreferences.TextBrightness);
        }
    }
}
