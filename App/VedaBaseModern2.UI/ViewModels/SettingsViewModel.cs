using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;
using VedaBaseModern.Core.Services;
using VedaBaseModern.UI.Services;
using Windows.UI;

namespace VedaBaseModern.UI.ViewModels
{
    /// <summary>
    /// Reading-experience preferences (Milestone 6). Each selection index maps
    /// 1:1 to its enum's declaration order (System/Light/Dark,
    /// Small/Medium/Large/ExtraLarge, Narrow/Comfortable/Wide,
    /// Compact/Comfortable/Relaxed) - see MILESTONE_6_SETTINGS_ARCHITECTURE.md.
    /// No SQLite access here; everything goes through ISettingsService.
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IResearchDataBackupService _backupService;
        private readonly IResearchSyncService _syncService;
        private readonly Action<Microsoft.UI.Xaml.ElementTheme> _applyThemeLive;

        // Set while LoadAsync/Reset are populating the bound properties from
        // storage, so the On*Changed hooks below don't immediately write the
        // same value straight back to user.db.
        private bool _suppressPersist;

        [ObservableProperty] private bool _isLoading = true;
        [ObservableProperty] private string _errorMessage = string.Empty;

        [ObservableProperty] private string _backupStatusMessage = string.Empty;
        [ObservableProperty] private string _backupErrorMessage = string.Empty;
        [ObservableProperty] private bool _isBackupOperationRunning;

        [ObservableProperty] private bool _isCloudSyncConfigured;
        [ObservableProperty] private string _cloudSyncStatusMessage = string.Empty;
        [ObservableProperty] private string _cloudSyncErrorMessage = string.Empty;
        [ObservableProperty] private bool _isSyncing;
        [ObservableProperty] private string _lastSyncDisplay = "Never";
        [ObservableProperty] private string _syncStatsDisplay = string.Empty;
        [ObservableProperty] private string _supabaseProjectUrlDisplay = string.Empty;
        [ObservableProperty] private string _userEmailDisplay = string.Empty;
        [ObservableProperty] private int _syncIntervalIndex;
        [ObservableProperty] private string _syncStateDisplay = "Not Configured";
        [ObservableProperty] private string _pendingChangesDisplay = string.Empty;
        [ObservableProperty] private string _lastBackupDisplay = "No local backups yet";
        [ObservableProperty] private string _backupLocationDisplay = string.Empty;
        [ObservableProperty] private string _diagnosticsStatusMessage = string.Empty;

        [ObservableProperty] private int _themeIndex;
        [ObservableProperty] public partial bool IsCustomThemeSelected { get; set; }
        public IReadOnlyList<string> CuratedPresetNames { get; } = new List<string>(CustomTheme.GetCuratedPresets().Keys);
        [ObservableProperty] public partial string SelectedPresetName { get; set; } = "Lotus Rose";
        [ObservableProperty] public partial CustomTheme CurrentCustomTheme { get; set; } = CustomTheme.CreateDarkDefault();
        [ObservableProperty] public partial int SelectedComponentIndex { get; set; }
        [ObservableProperty] public partial Color PickerColor { get; set; } = Color.FromArgb(255, 15, 40, 30);
        [ObservableProperty] public partial string HexCodeText { get; set; } = "#0F281E";
        [ObservableProperty] public partial string CustomThemeStatusMessage { get; set; } = string.Empty;
        [ObservableProperty] public partial int BaseThemeIndex { get; set; }
        [ObservableProperty] public partial SolidColorBrush CurrentComponentBrush { get; set; } = new(Color.FromArgb(255, 15, 40, 30));

        [ObservableProperty] public partial SolidColorBrush PreviewPageBackgroundBrush { get; set; } = new(Color.FromArgb(255, 15, 40, 30));
        [ObservableProperty] public partial SolidColorBrush PreviewCardBackgroundBrush { get; set; } = new(Color.FromArgb(255, 22, 56, 42));
        [ObservableProperty] public partial SolidColorBrush PreviewNavPaneBrush { get; set; } = new(Color.FromArgb(255, 10, 28, 21));
        [ObservableProperty] public partial SolidColorBrush PreviewPrimaryTextBrush { get; set; } = new(Color.FromArgb(255, 242, 244, 243));
        [ObservableProperty] public partial SolidColorBrush PreviewSecondaryTextBrush { get; set; } = new(Color.FromArgb(255, 168, 189, 180));
        [ObservableProperty] public partial SolidColorBrush PreviewAccentBrush { get; set; } = new(Color.FromArgb(255, 212, 175, 55));
        [ObservableProperty] public partial SolidColorBrush PreviewDividerBrush { get; set; } = new(Color.FromArgb(255, 35, 77, 60));

        private bool _isSyncingColor;
        [ObservableProperty] private int _fontSizeIndex;
        [ObservableProperty] private int _readingWidthIndex;
        [ObservableProperty] private int _lineSpacingIndex;
        [ObservableProperty] private bool _focusModeDefault;
        [ObservableProperty] private bool _showTransliteration;
        [ObservableProperty] private bool _showSynonyms;
        [ObservableProperty] private bool _showPurport;
        [ObservableProperty] private bool _showPronunciationGuide = true;

        [ObservableProperty] public partial int TextBrightness { get; set; } = 100;
        [ObservableProperty] public partial int TextBrightnessPresetIndex { get; set; } = 0;
        [ObservableProperty] public partial string TextBrightnessDisplay { get; set; } = "100% — Crisp White";
        [ObservableProperty] public partial SolidColorBrush ReadingBrightnessSampleBrush { get; set; } = new(Color.FromArgb(255, 242, 244, 243));

        public SettingsViewModel(
            ISettingsService settingsService,
            Action<Microsoft.UI.Xaml.ElementTheme> applyThemeLive,
            IResearchDataBackupService? backupService = null,
            IResearchSyncService? syncService = null)
        {
            _settingsService = settingsService;
            _applyThemeLive = applyThemeLive;
            _backupService = backupService ?? VedaBaseModern_UI.App.Current.BackupService;
            _syncService = syncService ?? VedaBaseModern_UI.App.Current.SyncService;
        }

        public async Task LoadAsync()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            try
            {
                var settings = await _settingsService.GetSettingsAsync();
                _suppressPersist = true;

                if (VedaBaseModern_UI.App.Current?.CustomThemeService != null)
                {
                    var customTheme = await VedaBaseModern_UI.App.Current.CustomThemeService.LoadThemeAsync();
                    CurrentCustomTheme = customTheme;
                    if (!string.IsNullOrEmpty(customTheme.Name))
                    {
                        SelectedPresetName = customTheme.Name;
                    }
                    UpdatePreviewBrushes();
                    UpdatePickerFromSelectedComponent();
                }

                ThemeIndex = (int)settings.Theme;
                IsCustomThemeSelected = (settings.Theme == AppTheme.Custom);
                FontSizeIndex = (int)settings.FontSize;
                ReadingWidthIndex = (int)settings.ReadingWidth;
                LineSpacingIndex = (int)settings.LineSpacing;
                FocusModeDefault = settings.FocusModeEnabled;
                ShowTransliteration = settings.ShowTransliteration;
                ShowSynonyms = settings.ShowSynonyms;
                ShowPurport = settings.ShowPurport;
                ShowPronunciationGuide = settings.ShowPronunciationGuide;

                if (VedaBaseModern_UI.App.Current?.ReadingPreferencesService != null)
                {
                    var prefs = await VedaBaseModern_UI.App.Current.ReadingPreferencesService.LoadPreferencesAsync();
                    TextBrightness = prefs.TextBrightness;
                    UpdateBrightnessDisplayAndPreview(prefs.TextBrightness);
                }

                await RefreshSyncStatusAsync();
                await RefreshBackupLocationInfoAsync();
            }
            catch (Exception ex)
            {
                // GetSettingsAsync itself already falls back to safe defaults for
                // a corrupt *value* (see SqliteSettingsService.ParseEnumSafe) - a
                // genuine exception here means user.db itself is unavailable
                // (locked/missing). Shown, not swallowed, since it means the
                // displayed controls may not reflect the real stored preferences.
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to load settings: {ex}");
                ErrorMessage = "Couldn't load your settings. Showing defaults for now.";
            }
            finally
            {
                _suppressPersist = false;
                IsLoading = false;
            }
        }

        partial void OnSelectedPresetNameChanged(string value)
        {
            if (_suppressPersist) return;
            if (!string.IsNullOrEmpty(value))
            {
                SelectPreset(value);
            }
        }

        partial void OnThemeIndexChanged(int value)
        {
            var theme = (AppTheme)value;
            IsCustomThemeSelected = (theme == AppTheme.Custom);

            if (_suppressPersist) return;

            if (VedaBaseModern_UI.App.Current != null)
            {
                if (theme == AppTheme.Custom)
                {
                    if (VedaBaseModern_UI.App.Current.MainWindowInstance != null)
                    {
                        VedaBaseModern_UI.App.Current.CustomThemeService.ApplyCustomTheme(
                            CurrentCustomTheme,
                            VedaBaseModern_UI.App.Current.MainWindowInstance);
                    }
                    VedaBaseModern_UI.App.Current.NotifyThemeChanged(AppTheme.Custom);
                }
                else
                {
                    var elemTheme = VedaBaseModern_UI.App.ToElementTheme(theme);
                    if (VedaBaseModern_UI.App.Current.MainWindowInstance != null)
                    {
                        VedaBaseModern_UI.App.Current.CustomThemeService.RemoveCustomTheme(
                            elemTheme,
                            VedaBaseModern_UI.App.Current.MainWindowInstance);
                    }
                    _applyThemeLive(elemTheme);
                    VedaBaseModern_UI.App.Current.NotifyThemeChanged(theme);
                }
            }

            _ = PersistSafeAsync(() => _settingsService.SetThemeAsync(theme), nameof(ThemeIndex));
        }

        partial void OnFontSizeIndexChanged(int value)
        {
            if (_suppressPersist) return;
            _ = PersistSafeAsync(() => _settingsService.SetFontSizeAsync((ReadingFontSize)value), nameof(FontSizeIndex));
        }

        partial void OnReadingWidthIndexChanged(int value)
        {
            if (_suppressPersist) return;
            _ = PersistSafeAsync(() => _settingsService.SetReadingWidthAsync((ReadingWidthOption)value), nameof(ReadingWidthIndex));
        }

        partial void OnLineSpacingIndexChanged(int value)
        {
            if (_suppressPersist) return;
            _ = PersistSafeAsync(() => _settingsService.SetLineSpacingAsync((ReadingLineSpacing)value), nameof(LineSpacingIndex));
        }

        partial void OnFocusModeDefaultChanged(bool value)
        {
            if (_suppressPersist) return;
            _ = PersistSafeAsync(() => _settingsService.SetFocusModeAsync(value), nameof(FocusModeDefault));
        }

        partial void OnShowTransliterationChanged(bool value)
        {
            if (_suppressPersist) return;
            _ = PersistSafeAsync(() => _settingsService.SetShowTransliterationAsync(value), nameof(ShowTransliteration));
        }

        partial void OnShowSynonymsChanged(bool value)
        {
            if (_suppressPersist) return;
            _ = PersistSafeAsync(() => _settingsService.SetShowSynonymsAsync(value), nameof(ShowSynonyms));
        }

        partial void OnShowPurportChanged(bool value)
        {
            if (_suppressPersist) return;
            _ = PersistSafeAsync(() => _settingsService.SetShowPurportAsync(value), nameof(ShowPurport));
        }

        partial void OnShowPronunciationGuideChanged(bool value)
        {
            if (_suppressPersist) return;
            _ = PersistSafeAsync(() => _settingsService.SetShowPronunciationGuideAsync(value), nameof(ShowPronunciationGuide));
        }

        partial void OnTextBrightnessChanged(int value)
        {
            UpdateBrightnessDisplayAndPreview(value);
            if (_suppressPersist) return;
            if (VedaBaseModern_UI.App.Current?.ReadingPreferencesService != null)
            {
                _ = VedaBaseModern_UI.App.Current.ReadingPreferencesService.SetTextBrightnessAsync(value);
            }
        }

        partial void OnTextBrightnessPresetIndexChanged(int value)
        {
            int targetBrightness = value switch
            {
                0 => 100,
                1 => 85,
                2 => 70,
                3 => 55,
                _ => -1
            };
            if (targetBrightness > 0 && TextBrightness != targetBrightness)
            {
                TextBrightness = targetBrightness;
            }
        }

        public void SelectBrightnessPreset(int percentage)
        {
            TextBrightness = percentage;
        }

        private void UpdateBrightnessDisplayAndPreview(int percentage)
        {
            TextBrightnessPresetIndex = percentage switch
            {
                100 => 0,
                85 => 1,
                70 => 2,
                55 => 3,
                _ => -1
            };

            string label = percentage switch
            {
                100 => "100% — Crisp White",
                85 => "85% — Soft White (Recommended for Night)",
                70 => "70% — Muted Grey (Gentle Contrast)",
                55 => "55% — Subdued Grey (Bedtime / Low Light)",
                _ => $"{percentage}% — Custom Contrast"
            };
            TextBrightnessDisplay = label;

            var baseColor = Color.FromArgb(255, 242, 244, 243);
            var dimmedColor = ReadingPreferencesService.DimColor(baseColor, percentage);
            ReadingBrightnessSampleBrush = new SolidColorBrush(dimmedColor);
        }

        private static async Task PersistSafeAsync(Func<Task> write, string settingName)
        {
            try
            {
                await write();
            }
            catch (Exception ex)
            {
                // A failed settings write loses only this one preference change -
                // never existing bookmarks/notes/history. Logged, not surfaced as
                // a hard error, consistent with the rest of the app's best-effort
                // user.db write handling.
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to persist {settingName}: {ex.Message}");
            }
        }

        partial void OnSelectedComponentIndexChanged(int value)
        {
            UpdatePickerFromSelectedComponent();
        }

        private void UpdatePickerFromSelectedComponent()
        {
            string hex = SelectedComponentIndex switch
            {
                0 => CurrentCustomTheme.PageBackground,
                1 => CurrentCustomTheme.CardBackground,
                2 => CurrentCustomTheme.NavigationPaneBackground,
                3 => CurrentCustomTheme.PrimaryText,
                4 => CurrentCustomTheme.SecondaryText,
                5 => CurrentCustomTheme.AccentColor,
                6 => CurrentCustomTheme.DividerColor,
                _ => CurrentCustomTheme.PageBackground
            };

            var color = CustomThemeService.ParseColor(hex, Color.FromArgb(255, 15, 40, 30));
            _isSyncingColor = true;
            try
            {
                PickerColor = color;
                CurrentComponentBrush = new SolidColorBrush(color);
                HexCodeText = CustomThemeService.ToHex(color, color.A != 255);
            }
            finally
            {
                _isSyncingColor = false;
            }
        }

        partial void OnPickerColorChanged(Color value)
        {
            if (_isSyncingColor) return;
            _isSyncingColor = true;
            try
            {
                string hex = CustomThemeService.ToHex(value, value.A != 255);
                HexCodeText = hex;
                CurrentComponentBrush = new SolidColorBrush(value);
                SetComponentColor(SelectedComponentIndex, hex);
                UpdatePreviewBrushes();
            }
            finally
            {
                _isSyncingColor = false;
            }
        }

        partial void OnHexCodeTextChanged(string value)
        {
            if (_isSyncingColor) return;
            if (string.IsNullOrWhiteSpace(value)) return;

            string clean = value.Trim().TrimStart('#');
            if (clean.Length is 3 or 4 or 6 or 8)
            {
                var transparent = Color.FromArgb(0, 0, 0, 0);
                var parsed = CustomThemeService.ParseColor(value, transparent);
                if (parsed != transparent || clean == "000" || clean == "000000" || clean == "00000000")
                {
                    _isSyncingColor = true;
                    try
                    {
                        PickerColor = parsed;
                        CurrentComponentBrush = new SolidColorBrush(parsed);
                        string normalizedHex = value.StartsWith('#') ? value.Trim() : $"#{value.Trim()}";
                        SetComponentColor(SelectedComponentIndex, normalizedHex);
                        UpdatePreviewBrushes();
                    }
                    finally
                    {
                        _isSyncingColor = false;
                    }
                }
            }
        }

        private void SetComponentColor(int componentIndex, string hex)
        {
            switch (componentIndex)
            {
                case 0: CurrentCustomTheme.PageBackground = hex; break;
                case 1: CurrentCustomTheme.CardBackground = hex; break;
                case 2: CurrentCustomTheme.NavigationPaneBackground = hex; break;
                case 3: CurrentCustomTheme.PrimaryText = hex; break;
                case 4: CurrentCustomTheme.SecondaryText = hex; break;
                case 5: CurrentCustomTheme.AccentColor = hex; break;
                case 6: CurrentCustomTheme.DividerColor = hex; break;
            }
        }

        partial void OnBaseThemeIndexChanged(int value)
        {
            CurrentCustomTheme.BaseTheme = (value == 1) ? "Light" : "Dark";
            if (ThemeIndex == 3 && VedaBaseModern_UI.App.Current?.MainWindowInstance != null)
            {
                VedaBaseModern_UI.App.Current.CustomThemeService.ApplyCustomTheme(
                    CurrentCustomTheme,
                    VedaBaseModern_UI.App.Current.MainWindowInstance);
                VedaBaseModern_UI.App.Current.NotifyThemeChanged(AppTheme.Custom);
            }
        }

        public void UpdatePreviewBrushes()
        {
            PreviewPageBackgroundBrush = new SolidColorBrush(CustomThemeService.ParseColor(CurrentCustomTheme.PageBackground, Color.FromArgb(255, 15, 40, 30)));
            PreviewCardBackgroundBrush = new SolidColorBrush(CustomThemeService.ParseColor(CurrentCustomTheme.CardBackground, Color.FromArgb(255, 22, 56, 42)));
            PreviewNavPaneBrush = new SolidColorBrush(CustomThemeService.ParseColor(CurrentCustomTheme.NavigationPaneBackground, Color.FromArgb(255, 10, 28, 21)));
            PreviewPrimaryTextBrush = new SolidColorBrush(CustomThemeService.ParseColor(CurrentCustomTheme.PrimaryText, Color.FromArgb(255, 242, 244, 243)));
            PreviewSecondaryTextBrush = new SolidColorBrush(CustomThemeService.ParseColor(CurrentCustomTheme.SecondaryText, Color.FromArgb(255, 168, 189, 180)));
            PreviewAccentBrush = new SolidColorBrush(CustomThemeService.ParseColor(CurrentCustomTheme.AccentColor, Color.FromArgb(255, 212, 175, 55)));
            PreviewDividerBrush = new SolidColorBrush(CustomThemeService.ParseColor(CurrentCustomTheme.DividerColor, Color.FromArgb(255, 35, 77, 60)));

            _isSyncingColor = true;
            try
            {
                BaseThemeIndex = string.Equals(CurrentCustomTheme.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
            finally
            {
                _isSyncingColor = false;
            }
        }

        [RelayCommand]
        public void CopyFromDark()
        {
            CurrentCustomTheme = CustomTheme.CreateDarkDefault();
            UpdatePreviewBrushes();
            UpdatePickerFromSelectedComponent();
            CustomThemeStatusMessage = "Colors copied from Dark theme. Adjust any component below!";

            if (ThemeIndex == 3 && VedaBaseModern_UI.App.Current?.MainWindowInstance != null)
            {
                _ = ApplyCustomThemeAsync();
            }
        }

        [RelayCommand]
        public void CopyFromLight()
        {
            CurrentCustomTheme = CustomTheme.CreateLightDefault();
            UpdatePreviewBrushes();
            UpdatePickerFromSelectedComponent();
            CustomThemeStatusMessage = "Colors copied from Light theme. Adjust any component below!";

            if (ThemeIndex == 3 && VedaBaseModern_UI.App.Current?.MainWindowInstance != null)
            {
                _ = ApplyCustomThemeAsync();
            }
        }


        [RelayCommand]
        public void SelectPreset(string presetKey)
        {
            var presets = CustomTheme.GetCuratedPresets();
            if (presets.TryGetValue(presetKey, out var preset))
            {
                CurrentCustomTheme = preset.Clone();
                if (SelectedPresetName != preset.Name)
                {
                    SelectedPresetName = preset.Name;
                }
                UpdatePreviewBrushes();
                UpdatePickerFromSelectedComponent();
                CustomThemeStatusMessage = $"Loaded preset: {preset.Name}";

                if (ThemeIndex == 3 && VedaBaseModern_UI.App.Current?.MainWindowInstance != null)
                {
                    _ = ApplyCustomThemeAsync();
                }
            }
        }

        [RelayCommand]
        public async Task ApplyCustomThemeAsync()
        {
            try
            {
                var service = VedaBaseModern_UI.App.Current.CustomThemeService;
                await service.SaveThemeAsync(CurrentCustomTheme);

                if (VedaBaseModern_UI.App.Current?.MainWindowInstance != null)
                {
                    service.ApplyCustomTheme(CurrentCustomTheme, VedaBaseModern_UI.App.Current.MainWindowInstance);
                    VedaBaseModern_UI.App.Current.NotifyThemeChanged(AppTheme.Custom);
                }

                if (ThemeIndex != 3)
                {
                    ThemeIndex = 3;
                }

                // Explicitly lock in Custom theme in user settings
                await _settingsService.SetThemeAsync(AppTheme.Custom);

                CustomThemeStatusMessage = "Custom theme applied to application successfully!";
            }
            catch (Exception ex)
            {
                CustomThemeStatusMessage = $"Failed to apply theme: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ResetCustomThemeAsync()
        {
            CurrentCustomTheme = CustomTheme.CreateDarkDefault();
            UpdatePreviewBrushes();
            UpdatePickerFromSelectedComponent();

            var service = VedaBaseModern_UI.App.Current.CustomThemeService;
            await service.SaveThemeAsync(CurrentCustomTheme);
            if (ThemeIndex == 3 && VedaBaseModern_UI.App.Current?.MainWindowInstance != null)
            {
                service.ApplyCustomTheme(CurrentCustomTheme, VedaBaseModern_UI.App.Current.MainWindowInstance);
                VedaBaseModern_UI.App.Current.NotifyThemeChanged(AppTheme.Custom);
            }
            CustomThemeStatusMessage = "Custom theme reset to default.";
        }

        [RelayCommand]
        public async Task ResetToDefaultsAsync()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            try
            {
                await _settingsService.ResetToDefaultsAsync();
                var defaults = new AppSettings();
                _suppressPersist = true;
                ThemeIndex = (int)defaults.Theme;
                FontSizeIndex = (int)defaults.FontSize;
                ReadingWidthIndex = (int)defaults.ReadingWidth;
                LineSpacingIndex = (int)defaults.LineSpacing;
                FocusModeDefault = defaults.FocusModeEnabled;
                ShowTransliteration = defaults.ShowTransliteration;
                ShowSynonyms = defaults.ShowSynonyms;
                ShowPurport = defaults.ShowPurport;
                ShowPronunciationGuide = defaults.ShowPronunciationGuide;
                // Note: assigning ThemeIndex above already invoked
                // OnThemeIndexChanged, which applies the theme live - no
                // separate call needed here.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to reset settings: {ex}");
                ErrorMessage = "Couldn't reset your settings. Please try again.";
            }
            finally
            {
                _suppressPersist = false;
                IsLoading = false;
            }
        }

        public async Task<bool> ExportBackupAsync(string targetFilePath)
        {
            BackupStatusMessage = string.Empty;
            BackupErrorMessage = string.Empty;
            IsBackupOperationRunning = true;

            try
            {
                await _backupService.ExportBackupToFileAsync(targetFilePath);
                string filename = System.IO.Path.GetFileName(targetFilePath);
                BackupStatusMessage = $"Backup saved successfully to {filename}";
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Export failed: {ex}");
                BackupErrorMessage = $"Failed to export backup: {ex.Message}";
                return false;
            }
            finally
            {
                IsBackupOperationRunning = false;
            }
        }

        public async Task<BackupValidationResult> InspectBackupAsync(string sourceFilePath)
        {
            BackupStatusMessage = string.Empty;
            BackupErrorMessage = string.Empty;
            return await _backupService.ValidateBackupFileAsync(sourceFilePath);
        }

        public async Task<bool> RestoreBackupAsync(string sourceFilePath, BackupImportMode mode)
        {
            BackupStatusMessage = string.Empty;
            BackupErrorMessage = string.Empty;
            IsBackupOperationRunning = true;

            try
            {
                var result = await _backupService.RestoreBackupAsync(sourceFilePath, mode);
                if (result.Success)
                {
                    string countsSummary = result.ImportedCounts != null
                        ? $" ({result.ImportedCounts.Bookmarks} bookmarks, {result.ImportedCounts.Highlights} highlights, {result.ImportedCounts.Notes} notes, {result.ImportedCounts.Collections} collections)"
                        : string.Empty;

                    string snapshotInfo = !string.IsNullOrEmpty(result.SnapshotPath)
                        ? $" Safety snapshot saved to {System.IO.Path.GetFileName(result.SnapshotPath)}."
                        : string.Empty;

                    BackupStatusMessage = $"Restore completed successfully!{countsSummary}{snapshotInfo}";
                    
                    // Reload current preferences into ViewModel
                    await LoadAsync();
                    return true;
                }
                else
                {
                    BackupErrorMessage = result.ErrorMessage ?? "Restore operation failed.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Restore failed: {ex}");
                BackupErrorMessage = $"Failed to restore backup: {ex.Message}";
                return false;
            }
            finally
            {
                IsBackupOperationRunning = false;
            }
        }

        public async Task RefreshSyncStatusAsync()
        {
            try
            {
                var status = await _syncService.GetSyncStatusAsync();
                IsCloudSyncConfigured = status.IsConfigured;
                SupabaseProjectUrlDisplay = status.ProjectUrl ?? string.Empty;
                UserEmailDisplay = !string.IsNullOrWhiteSpace(status.UserEmail)
                    ? status.UserEmail
                    : "Anon / Project Scoped Mode";

                LastSyncDisplay = status.LastSyncUtc.HasValue
                    ? status.LastSyncUtc.Value.ToLocalTime().ToString("g")
                    : "Never";
                SyncStatsDisplay = $"Uploaded: {status.UploadedCount} | Downloaded: {status.DownloadedCount} | Conflicts: {status.ConflictCount}";
                CloudSyncStatusMessage = status.LastSyncMessage;

                // Human-readable state text (Phase 7) - one authoritative
                // source (SyncStatusInfo.State) instead of re-deriving the
                // same condition from booleans in multiple places.
                SyncStateDisplay = status.State switch
                {
                    SyncState.NotConfigured => "Not Configured (Working 100% Offline)",
                    SyncState.ConfiguredNeverSynced => "Connected — never synced yet",
                    SyncState.Syncing => "Syncing…",
                    SyncState.Synced => "Connected — up to date",
                    SyncState.LocalChangesPending => "Connected — local changes pending",
                    SyncState.Offline => "Offline",
                    SyncState.AuthenticationFailure => "Authentication failed — check credentials",
                    SyncState.PermissionFailure => "Access denied — check Supabase permissions",
                    SyncState.NetworkFailure => "Network error — will retry on next sync",
                    SyncState.SyncFailed => "Last sync failed",
                    _ => "Unknown"
                };
                PendingChangesDisplay = status.PendingLocalChanges == 1
                    ? "1 local change pending"
                    : $"{status.PendingLocalChanges} local changes pending";

                _suppressPersist = true;
                SyncIntervalIndex = MinutesToIntervalIndex(status.SyncIntervalMinutes);
                _suppressPersist = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to refresh sync status: {ex.Message}");
            }
        }

        partial void OnSyncIntervalIndexChanged(int value)
        {
            if (_suppressPersist) return;
            int minutes = IntervalIndexToMinutes(value);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _syncService.SetSyncIntervalAsync(minutes);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Settings] Failed to update sync interval: {ex.Message}");
                }
            });
        }

        private static int MinutesToIntervalIndex(int minutes) => minutes switch
        {
            15 => 1,
            30 => 2,
            60 => 3,
            360 => 4,
            1440 => 5,
            _ => 0
        };

        private static int IntervalIndexToMinutes(int index) => index switch
        {
            1 => 15,
            2 => 30,
            3 => 60,
            4 => 360,
            5 => 1440,
            _ => 0
        };

        public async Task<bool> SyncNowAsync()
        {
            CloudSyncStatusMessage = string.Empty;
            CloudSyncErrorMessage = string.Empty;
            IsSyncing = true;

            try
            {
                var result = await _syncService.SyncNowAsync();
                if (result.Success)
                {
                    CloudSyncStatusMessage = $"Sync successful! ({result.UploadedCount} uploaded, {result.DownloadedCount} downloaded, {result.ConflictCount} conflicts resolved).";
                    await RefreshSyncStatusAsync();
                    return true;
                }
                else
                {
                    CloudSyncErrorMessage = result.ErrorMessage ?? "Sync failed.";
                    await RefreshSyncStatusAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] SyncNow failed: {ex}");
                CloudSyncErrorMessage = $"Sync failed: {ex.Message}";
                return false;
            }
            finally
            {
                IsSyncing = false;
            }
        }

        public async Task<SyncConnectionTestResult> ConnectSupabaseAsync(
            string url,
            string apiKey,
            string? email = null,
            string? password = null)
        {
            CloudSyncStatusMessage = string.Empty;
            CloudSyncErrorMessage = string.Empty;
            IsSyncing = true;

            try
            {
                var testResult = await _syncService.ConfigureSupabaseAsync(url, apiKey, email, password);
                if (testResult.Success)
                {
                    await RefreshSyncStatusAsync();
                    CloudSyncStatusMessage = "Connected to Supabase project successfully!";
                }
                else
                {
                    CloudSyncErrorMessage = testResult.ErrorMessage ?? "Failed to connect to Supabase project.";
                }
                return testResult;
            }
            catch (Exception ex)
            {
                CloudSyncErrorMessage = $"Connection error: {ex.Message}";
                return new SyncConnectionTestResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                IsSyncing = false;
            }
        }

        public async Task DisconnectSupabaseAsync()
        {
            IsSyncing = true;
            try
            {
                await _syncService.DisconnectProviderAsync();
                await RefreshSyncStatusAsync();
                CloudSyncStatusMessage = "Disconnected from cloud sync. Your local research data is preserved intact.";
            }
            catch (Exception ex)
            {
                CloudSyncErrorMessage = $"Disconnect error: {ex.Message}";
            }
            finally
            {
                IsSyncing = false;
            }
        }

        /// <summary>
        /// Phase 7 project-switch safety check - the Page must call this
        /// BEFORE calling ConnectSupabaseAsync and, if it returns true, show
        /// an explicit confirmation naming the risk (all current local
        /// research data will be treated as new data for the target project
        /// on the next sync) rather than connecting silently.
        /// </summary>
        public async Task<bool> WouldSwitchProjectAsync(string url, string? email)
        {
            try
            {
                return await _syncService.WouldSwitchProjectAsync(url, email);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] WouldSwitchProjectAsync check failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ExportSyncDiagnosticsAsync(string targetFilePath)
        {
            DiagnosticsStatusMessage = string.Empty;
            try
            {
                await _syncService.ExportDiagnosticsAsync(targetFilePath);
                DiagnosticsStatusMessage = $"Diagnostics exported to {System.IO.Path.GetFileName(targetFilePath)} (no credentials or tokens included).";
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Diagnostics export failed: {ex}");
                DiagnosticsStatusMessage = $"Failed to export diagnostics: {ex.Message}";
                return false;
            }
        }

        public async Task<bool> CreateSafetySnapshotAsync()
        {
            BackupStatusMessage = string.Empty;
            BackupErrorMessage = string.Empty;
            IsBackupOperationRunning = true;
            try
            {
                string path = await _backupService.CreateLocalSnapshotAsync();
                if (string.IsNullOrEmpty(path))
                {
                    BackupErrorMessage = "Could not create a safety snapshot (local database not found).";
                    return false;
                }
                BackupStatusMessage = $"Safety snapshot created: {System.IO.Path.GetFileName(path)}";
                await RefreshBackupLocationInfoAsync();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Safety snapshot failed: {ex}");
                BackupErrorMessage = $"Failed to create safety snapshot: {ex.Message}";
                return false;
            }
            finally
            {
                IsBackupOperationRunning = false;
            }
        }

        /// <summary>
        /// Populates LastBackupDisplay/BackupLocationDisplay by inspecting the
        /// well-known Backups folder next to user.db - read-only, never
        /// creates or modifies anything (see ResearchDataBackupService.
        /// CreateLocalSnapshotAsync for the folder convention this mirrors).
        /// </summary>
        public Task RefreshBackupLocationInfoAsync()
        {
            // Deliberately synchronous (no Task.Run) despite the file-system
            // checks: this is trivial local I/O (one Directory.Exists, one
            // small GetFiles listing), and setting [ObservableProperty]-backed
            // fields from a background thread does not reliably marshal to
            // the UI thread for x:Bind - a real bug caught during Phase 7
            // manual QA (BackupLocationDisplay silently stayed at its default
            // empty value while LastBackupDisplay appeared to work only
            // because its default happened to equal the common-case value).
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string backupDir = System.IO.Path.Combine(localAppData, "VedaBaseModern", "Backups");
                BackupLocationDisplay = backupDir;

                if (!System.IO.Directory.Exists(backupDir))
                {
                    LastBackupDisplay = "No local backups yet";
                    return Task.CompletedTask;
                }

                var newest = new System.IO.DirectoryInfo(backupDir)
                    .GetFiles()
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                LastBackupDisplay = newest != null
                    ? $"{newest.LastWriteTime:g} ({newest.Name})"
                    : "No local backups yet";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to refresh backup location info: {ex.Message}");
            }
            return Task.CompletedTask;
        }
    }
}
