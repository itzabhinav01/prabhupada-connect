using System;
using System.Collections.Generic;

namespace VedaBaseModern.Core.Models
{
    public enum AppTheme { System, Light, Dark, Custom }

    public enum ReadingFontSize { Small, Medium, Large, ExtraLarge }

    public enum ReadingWidthOption { Narrow, Comfortable, Wide }

    public enum ReadingLineSpacing { Compact, Comfortable, Relaxed }

    /// <summary>
    /// The user's personal reading-experience preferences. Lives entirely in
    /// user.db (UserSettings table) - never in corpus_v6_final.db. Contains only
    /// scalar preference values, never corpus content. See
    /// MILESTONE_6_SETTINGS_ARCHITECTURE.md for defaults rationale and the
    /// enum-to-pixel mapping (kept in the UI project, not here, since that
    /// mapping is presentation logic).
    /// </summary>
    public class AppSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.System;
        public ReadingFontSize FontSize { get; set; } = ReadingFontSize.Medium;
        public ReadingWidthOption ReadingWidth { get; set; } = ReadingWidthOption.Comfortable;
        public ReadingLineSpacing LineSpacing { get; set; } = ReadingLineSpacing.Comfortable;
        public bool FocusModeEnabled { get; set; } = false;
        public bool ShowTransliteration { get; set; } = true;
        public bool ShowSynonyms { get; set; } = true;
        public bool ShowPurport { get; set; } = true;
        public bool ShowPronunciationGuide { get; set; } = true;
        public List<HighlightColorItem> HighlightPalette { get; set; } = HighlightColorHelper.CreateDefaultPalette();
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
