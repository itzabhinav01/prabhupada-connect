using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Repositories
{
    public class SqliteSettingsService : ISettingsService
    {
        private readonly string _connectionString;

        // Tiny in-memory cache (section 13 of the milestone task) - settings are
        // small and read frequently (once per Reading View load), so caching
        // avoids a SQLite round-trip on every record navigation. This is NOT a
        // corpus cache - it holds only these five scalar preference values.
        private AppSettings? _cached;

        public SqliteSettingsService(string dbPath)
        {
            _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate";
        }

        public async Task<AppSettings> GetSettingsAsync()
        {
            if (_cached != null) return _cached;

            var settings = await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                try
                {
                    cmd.CommandText = "SELECT Theme, FontSize, ReadingWidth, LineSpacing, FocusModeEnabled, ShowTransliteration, ShowSynonyms, ShowPurport, UpdatedUtc, ShowPronunciationGuide FROM UserSettings WHERE Id = 1";
                }
                catch
                {
                    cmd.CommandText = "SELECT Theme, FontSize, ReadingWidth, LineSpacing, FocusModeEnabled, ShowTransliteration, ShowSynonyms, ShowPurport, UpdatedUtc FROM UserSettings WHERE Id = 1";
                }
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    bool showPronunciation = true;
                    if (reader.FieldCount > 9 && !reader.IsDBNull(9))
                    {
                        showPronunciation = reader.GetInt32(9) != 0;
                    }

                    return new AppSettings
                    {
                        Theme = ParseEnumSafe(reader.IsDBNull(0) ? null : reader.GetString(0), AppTheme.System),
                        FontSize = ParseEnumSafe(reader.IsDBNull(1) ? null : reader.GetString(1), ReadingFontSize.Medium),
                        ReadingWidth = ParseEnumSafe(reader.IsDBNull(2) ? null : reader.GetString(2), ReadingWidthOption.Comfortable),
                        LineSpacing = ParseEnumSafe(reader.IsDBNull(3) ? null : reader.GetString(3), ReadingLineSpacing.Comfortable),
                        FocusModeEnabled = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                        ShowTransliteration = reader.IsDBNull(5) || reader.GetInt32(5) != 0,
                        ShowSynonyms = reader.IsDBNull(6) || reader.GetInt32(6) != 0,
                        ShowPurport = reader.IsDBNull(7) || reader.GetInt32(7) != 0,
                        ShowPronunciationGuide = showPronunciation,
                        UpdatedUtc = reader.IsDBNull(8) ? DateTime.UtcNow : (DateTime.TryParse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt) ? dt : DateTime.UtcNow)
                    };
                }
                // No row at all (should not happen post-migration, but never
                // fabricate a crash over it) - safe documented defaults.
                return new AppSettings();
            });

            _cached = settings;
            return settings;
        }

        // Never Enum.Parse (throws on bad input). A corrupt/unrecognized stored
        // value silently falls back to the documented default - see
        // MILESTONE_6_SETTINGS_ARCHITECTURE.md section 7 / task section 27.
        //
        // Enum.TryParse alone is NOT sufficient: for a purely numeric string
        // (e.g. the task's own example, "999999999"), TryParse succeeds and
        // returns an enum value whose underlying int has no matching named
        // member at all - it does not validate that the value is one of the
        // type's declared members. Enum.IsDefined closes that gap. Found and
        // fixed via this milestone's own validation harness, which reproduced
        // that exact scenario.
        private static T ParseEnumSafe<T>(string? value, T fallback) where T : struct, Enum
        {
            if (!string.IsNullOrWhiteSpace(value)
                && Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
                && Enum.IsDefined(typeof(T), parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private async Task UpdateAsync(string column, object value)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string nowIso = DateTime.UtcNow.ToString("O");
                // column is always one of a small fixed set of literal names from
                // this file, never user input - safe to interpolate; the VALUE
                // itself is always parameterized.
                cmd.CommandText = $"UPDATE UserSettings SET {column} = $value, UpdatedUtc = $now WHERE Id = 1";
                cmd.Parameters.AddWithValue("$value", value);
                cmd.Parameters.AddWithValue("$now", nowIso);
                cmd.ExecuteNonQuery();
            });
            if (_cached != null) _cached.UpdatedUtc = DateTime.UtcNow;
        }

        public async Task SetThemeAsync(AppTheme theme)
        {
            await UpdateAsync("Theme", theme.ToString());
            if (_cached != null) _cached.Theme = theme;
        }

        public async Task SetFontSizeAsync(ReadingFontSize size)
        {
            await UpdateAsync("FontSize", size.ToString());
            if (_cached != null) _cached.FontSize = size;
        }

        public async Task SetReadingWidthAsync(ReadingWidthOption width)
        {
            await UpdateAsync("ReadingWidth", width.ToString());
            if (_cached != null) _cached.ReadingWidth = width;
        }

        public async Task SetLineSpacingAsync(ReadingLineSpacing spacing)
        {
            await UpdateAsync("LineSpacing", spacing.ToString());
            if (_cached != null) _cached.LineSpacing = spacing;
        }

        public async Task SetFocusModeAsync(bool enabled)
        {
            await UpdateAsync("FocusModeEnabled", enabled ? 1 : 0);
            if (_cached != null) _cached.FocusModeEnabled = enabled;
        }

        public async Task SetShowTransliterationAsync(bool enabled)
        {
            await UpdateAsync("ShowTransliteration", enabled ? 1 : 0);
            if (_cached != null) _cached.ShowTransliteration = enabled;
        }

        public async Task SetShowSynonymsAsync(bool enabled)
        {
            await UpdateAsync("ShowSynonyms", enabled ? 1 : 0);
            if (_cached != null) _cached.ShowSynonyms = enabled;
        }

        public async Task SetShowPurportAsync(bool enabled)
        {
            await UpdateAsync("ShowPurport", enabled ? 1 : 0);
            if (_cached != null) _cached.ShowPurport = enabled;
        }

        public async Task SetShowPronunciationGuideAsync(bool enabled)
        {
            try
            {
                await UpdateAsync("ShowPronunciationGuide", enabled ? 1 : 0);
            }
            catch { }
            if (_cached != null) _cached.ShowPronunciationGuide = enabled;
        }

        public async Task ResetToDefaultsAsync()
        {
            var defaults = new AppSettings();
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string nowIso = DateTime.UtcNow.ToString("O");
                cmd.CommandText = @"
                    UPDATE UserSettings SET
                        Theme = $theme,
                        FontSize = $fontSize,
                        ReadingWidth = $width,
                        LineSpacing = $spacing,
                        FocusModeEnabled = $focus,
                        ShowTransliteration = $showTranslit,
                        ShowSynonyms = $showSyn,
                        ShowPurport = $showPurport,
                        ShowPronunciationGuide = $showPronun,
                        UpdatedUtc = $now
                    WHERE Id = 1";
                cmd.Parameters.AddWithValue("$theme", defaults.Theme.ToString());
                cmd.Parameters.AddWithValue("$fontSize", defaults.FontSize.ToString());
                cmd.Parameters.AddWithValue("$width", defaults.ReadingWidth.ToString());
                cmd.Parameters.AddWithValue("$spacing", defaults.LineSpacing.ToString());
                cmd.Parameters.AddWithValue("$focus", defaults.FocusModeEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$showTranslit", defaults.ShowTransliteration ? 1 : 0);
                cmd.Parameters.AddWithValue("$showSyn", defaults.ShowSynonyms ? 1 : 0);
                cmd.Parameters.AddWithValue("$showPurport", defaults.ShowPurport ? 1 : 0);
                cmd.Parameters.AddWithValue("$showPronun", defaults.ShowPronunciationGuide ? 1 : 0);
                cmd.Parameters.AddWithValue("$now", nowIso);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // Fallback for schema before column added
                    cmd.CommandText = @"
                        UPDATE UserSettings SET
                            Theme = $theme,
                            FontSize = $fontSize,
                            ReadingWidth = $width,
                            LineSpacing = $spacing,
                            FocusModeEnabled = $focus,
                            ShowTransliteration = $showTranslit,
                            ShowSynonyms = $showSyn,
                            ShowPurport = $showPurport,
                            UpdatedUtc = $now
                        WHERE Id = 1";
                    cmd.ExecuteNonQuery();
                }
            });
            defaults.UpdatedUtc = DateTime.UtcNow;
            _cached = defaults;
        }
    }
}
