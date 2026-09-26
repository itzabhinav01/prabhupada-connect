using System.Collections.Generic;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Repositories
{
    /// <summary>
    /// Reading-experience preferences (Milestone 6). Backed by the same user.db
    /// file as IUserRepository, but migration ownership stays exclusively with
    /// SqliteUserRepository.InitializeAsync() - this service only ever reads/
    /// writes the single UserSettings row, never DDL. See
    /// MILESTONE_6_SETTINGS_ARCHITECTURE.md.
    /// </summary>
    public interface ISettingsService
    {
        Task<AppSettings> GetSettingsAsync();
        Task SetThemeAsync(AppTheme theme);
        Task SetFontSizeAsync(ReadingFontSize size);
        Task SetReadingWidthAsync(ReadingWidthOption width);
        Task SetLineSpacingAsync(ReadingLineSpacing spacing);
        Task SetFocusModeAsync(bool enabled);
        Task SetShowTransliterationAsync(bool enabled);
        Task SetShowSynonymsAsync(bool enabled);
        Task SetShowPurportAsync(bool enabled);
        Task SetShowPronunciationGuideAsync(bool enabled);
        Task SetHighlightPaletteAsync(List<HighlightColorItem> palette);
        Task ResetToDefaultsAsync();
    }
}
