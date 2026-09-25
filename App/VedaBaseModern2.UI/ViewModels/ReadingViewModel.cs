using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;
using VedaBaseModern.UI.Messages;

namespace VedaBaseModern.UI.ViewModels
{
    // Parameter DTOs for the highlight RelayCommands below - CommunityToolkit.Mvvm
    // generates a single-parameter command per [RelayCommand] method, and these
    // actions genuinely need more than one value at once.
    public record HighlightRequest(string Field, int StartOffset, int Length, string SelectedText, HighlightColor Color);
    public record HighlightColorChange(Highlight Highlight, HighlightColor NewColor);

    public partial class ReadingViewModel : ObservableObject
    {
        private readonly ICorpusRepository _repository;
        private readonly IUserRepository _userRepository;
        private readonly ISettingsService _settingsService;

        [ObservableProperty]
        private CorpusRecord _currentRecord = new CorpusRecord();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isBookmarked;

        [ObservableProperty]
        private string? _currentBookmarkCollectionId;

        [ObservableProperty]
        private string _currentBookmarkCollectionName = "Uncategorized";

        public ObservableCollection<BookmarkCollection> Collections { get; } = new();

        [ObservableProperty]
        private string _newNoteContent = string.Empty;

        // Set by ReadingPage when "Add Note" is invoked from an active text
        // selection (see AddNoteFromSelection_Click) so the resulting note
        // carries the same (RecordKey, Field, StartOffset, Length) anchor a
        // Highlight would - the canonical source of truth for "where this
        // note points", never the quoted text alone. Cleared after every
        // AddNoteAsync call (successful or not) so a later record-level note
        // (no selection) doesn't accidentally inherit a stale anchor.
        private string? _pendingNoteField;
        private int _pendingNoteStartOffset = -1;
        private int _pendingNoteLength = -1;

        public void SetPendingNoteAnchor(string field, int startOffset, int length)
        {
            _pendingNoteField = field;
            _pendingNoteStartOffset = startOffset;
            _pendingNoteLength = length;
        }

        // ---- Reading typography (Milestone 6) ----
        // Computed from ISettingsService on every LoadRecordAsync call (cheap -
        // settings are cached in-memory after the first real read). See
        // MILESTONE_6_SETTINGS_ARCHITECTURE.md for the exact mapping tables and
        // why LineHeight is applied to Latin-script blocks only, never Devanagari.
        [ObservableProperty] private double _devanagariFontSize = 24;
        [ObservableProperty] private double _transliterationFontSize = 18;
        [ObservableProperty] private double _transliterationLineHeight = 27;
        [ObservableProperty] private double _synonymsFontSize = 16;
        [ObservableProperty] private double _synonymsLineHeight = 24;
        [ObservableProperty] private double _translationFontSize = 18;
        [ObservableProperty] private double _translationLineHeight = 27;
        [ObservableProperty] private double _purportFontSize = 18;
        [ObservableProperty] private double _purportLineHeight = 27;

        // In-session reader zoom (Ctrl+/-/0 and the toolbar A-/100%/A+
        // controls) - layered ON TOP of the persisted FontSize setting above,
        // not a replacement for it. Deliberately NOT reset by
        // ApplyReadingSettingsAsync (which reruns on every single verse
        // navigation) so zooming in once stays zoomed in as the reader pages
        // through a chapter; it only resets via ResetZoom or a fresh app
        // launch. _cachedFontScale/_cachedSpacingMultiplier are this session's
        // last-computed settings-derived multipliers, captured so Zoom*() can
        // recompute font sizes synchronously - no settings re-read, no
        // LoadRecordAsync, no touching CurrentRecord/Notes/Highlights/scroll
        // position, just the four FontSize/LineHeight property writes
        // themselves via ApplyFontSizes().
        [ObservableProperty] public partial double ZoomMultiplier { get; set; } = 1.0;
        private double _cachedFontScale = 1.0;
        private double _cachedSpacingMultiplier = 1.5;
        private const double ZoomStep = 0.10;
        private const double ZoomMin = 0.7;
        private const double ZoomMax = 1.8;

        [ObservableProperty] private double _contentMaxWidth = 720;
        [ObservableProperty] private bool _showTransliteration = true;
        [ObservableProperty] private bool _showSynonyms = true;
        [ObservableProperty] private bool _showPurport = true;
        [ObservableProperty] private bool _showPronunciationGuide = true;

        // ---- Focus Mode (Milestone 6) ----
        // Live, in-session toggle - never touches CurrentRecord, never navigates.
        [ObservableProperty] private bool _isFocusMode;
        private bool _focusModeInitializedFromSettings;

        // ---- Continuous-Chapter Reading View (Phase 3) ----
        [ObservableProperty] private bool _isContinuousChapter;
        [ObservableProperty] private ObservableCollection<CorpusRecord> _chapterRecords = new();
        [ObservableProperty] private string _chapterHeader = string.Empty;
        [ObservableProperty] private string _chapterSubtitle = string.Empty;
        [ObservableProperty] private string _breadcrumbText = string.Empty;
        [ObservableProperty] private ObservableCollection<BreadcrumbSegment> _breadcrumbSegments = new();
        [ObservableProperty] private bool _isLoadingChapter;

        partial void OnBreadcrumbTextChanged(string value)
        {
            UpdateBreadcrumbSegments(value);
        }

        public void UpdateBreadcrumbSegments(string breadcrumb)
        {
            BreadcrumbSegments.Clear();
            if (string.IsNullOrWhiteSpace(breadcrumb)) return;

            var parts = breadcrumb.Split(new[] { " › ", "›" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                BreadcrumbSegments.Add(new BreadcrumbSegment
                {
                    Text = parts[i].Trim(),
                    Index = i,
                    TotalSegments = parts.Length,
                    ShowChevron = (i < parts.Length - 1)
                });
            }
        }

        public ICorpusRepository Repository => _repository;

        public ObservableCollection<UserNote> Notes { get; } = new();

        // ---- Precision Highlights (Phase 4.7) ----
        // Exact character-range annotations, replacing Phase 4.6's
        // whole-block highlighting. One ObservableCollection per known
        // content block (the same 4 blocks Reading View has always shown);
        // the View (ReadingPage.xaml.cs) owns the actual RichEditBox
        // controls and re-applies CharacterFormat.BackgroundColor from these
        // collections whenever they change - the ViewModel never touches a
        // UI control directly, keeping this properly MVVM.
        public ObservableCollection<Highlight> DevanagariHighlights { get; } = new();
        public ObservableCollection<Highlight> TransliterationHighlights { get; } = new();
        public ObservableCollection<Highlight> SynonymsHighlights { get; } = new();
        public ObservableCollection<Highlight> TranslationHighlights { get; } = new();
        public ObservableCollection<Highlight> PurportHighlights { get; } = new();

        // Raised after ANY highlight add/remove/recolor for a given Field,
        // so the View knows exactly which RichEditBox to re-paint rather
        // than re-applying formatting to all four on every change.
        public event Action<string>? HighlightsChangedForField;

        // Phase 4.9.5: Granular lifecycle events for incremental in-memory range formatting
        // (zero document rebuilds, zero full text re-parsing).
        public event Action<Highlight>? HighlightAdded;
        public event Action<Highlight>? HighlightRemoved;
        public event Action<Highlight>? HighlightColorChanged;

        [ObservableProperty] private string _highlightStatusMessage = string.Empty;

        // Public - the View (ReadingPage.xaml.cs) also needs this mapping to
        // find which collection backs a given RichEditBox's Tag.
        public ObservableCollection<Highlight> CollectionFor(string field) => field switch
        {
            "Devanagari" => DevanagariHighlights,
            "Transliteration" => TransliterationHighlights,
            "Synonyms" => SynonymsHighlights,
            "Translation" => TranslationHighlights,
            "Purport" => PurportHighlights,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown highlight field")
        };

        private async Task LoadHighlightsAsync(string recordKey)
        {
            DevanagariHighlights.Clear();
            TransliterationHighlights.Clear();
            SynonymsHighlights.Clear();
            TranslationHighlights.Clear();
            PurportHighlights.Clear();
            try
            {
                var highlights = await _userRepository.GetHighlightsAsync(recordKey);
                foreach (var h in highlights)
                {
                    // A highlight whose Field no longer matches one of the 4
                    // known blocks (should never happen, but a corrupt/
                    // hand-edited row must never crash Reading View) is
                    // simply skipped here - it is still visible and
                    // manageable from the Personal Workspace Highlights page,
                    // which reads directly from the repository, not from
                    // these per-field collections.
                    try { CollectionFor(h.Field).Add(h); }
                    catch (ArgumentOutOfRangeException) { /* unknown field, skip */ }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to load highlights for '{recordKey}': {ex}");
            }
        }

        // Deterministic overlap policy (Phase 4.7, explicit product
        // decision, documented in PHASE_4_7_PRECISION_HIGHLIGHTING_REPORT.md
        // §9): a new highlight is REJECTED - not merged, not silently
        // allowed - if its range intersects any EXISTING highlight in the
        // same field. Chosen over "replace the old highlight" because
        // silently deleting a prior research annotation the user may not
        // have meant to touch is a worse failure mode than a clear, ignorable
        // status message asking them to remove the old one first.
        private static bool RangesOverlap(int aStart, int aLen, int bStart, int bLen) =>
            Highlight.RangesOverlap(aStart, aLen, bStart, bLen);

        [RelayCommand]
        public async Task AddHighlightAsync(HighlightRequest request)
        {
            if (string.IsNullOrEmpty(CurrentRecord?.RecordKey)) return;
            HighlightStatusMessage = string.Empty;

            var existing = CollectionFor(request.Field);
            foreach (var h in existing)
            {
                if (h.OverlapsWith(request.StartOffset, request.Length))
                {
                    HighlightStatusMessage = "That text already contains a highlight. Remove the existing highlight first.";
                    return;
                }
            }

            try
            {
                var created = await _userRepository.AddHighlightAsync(
                    CurrentRecord.RecordKey, request.Field, request.StartOffset, request.Length, request.SelectedText, request.Color);
                existing.Add(created);
                HighlightAdded?.Invoke(created);
                HighlightsChangedForField?.Invoke(request.Field);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to add highlight: {ex}");
                HighlightStatusMessage = "Couldn't save that highlight. Please try again.";
            }
        }

        [RelayCommand]
        public async Task RemoveHighlightAsync(Highlight highlight)
        {
            try
            {
                await _userRepository.RemoveHighlightAsync(highlight.Id);
                CollectionFor(highlight.Field).Remove(highlight);
                HighlightRemoved?.Invoke(highlight);
                HighlightsChangedForField?.Invoke(highlight.Field);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to remove highlight: {ex}");
                HighlightStatusMessage = "Couldn't remove that highlight. Please try again.";
            }
        }

        [RelayCommand]
        public async Task ChangeHighlightColorAsync(HighlightColorChange change)
        {
            try
            {
                await _userRepository.UpdateHighlightColorAsync(change.Highlight.Id, change.NewColor);
                change.Highlight.Color = change.NewColor;
                HighlightColorChanged?.Invoke(change.Highlight);
                HighlightsChangedForField?.Invoke(change.Highlight.Field);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to change highlight color: {ex}");
                HighlightStatusMessage = "Couldn't change that highlight's color. Please try again.";
            }
        }

        public async Task<List<Highlight>> GetHighlightsForRecordAsync(string recordKey)
        {
            try
            {
                return await _userRepository.GetHighlightsAsync(recordKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to get highlights for '{recordKey}': {ex}");
                return new List<Highlight>();
            }
        }

        public async Task<List<Highlight>> GetHighlightsForChapterAsync(IEnumerable<string> recordKeys)
        {
            var list = new List<Highlight>();
            try
            {
                foreach (var key in recordKeys)
                {
                    var hls = await _userRepository.GetHighlightsAsync(key);
                    if (hls != null && hls.Count > 0)
                    {
                        list.AddRange(hls);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to get highlights for chapter: {ex}");
            }
            return list;
        }

        public ReadingViewModel(ICorpusRepository repository, IUserRepository userRepository, ISettingsService settingsService)
        {
            _repository = repository;
            _userRepository = userRepository;
            _settingsService = settingsService;
        }

        public async Task LoadRecordAsync(string recordKey)
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            CurrentRecord = new CorpusRecord();

            try
            {
                await ApplyReadingSettingsAsync();

                var record = await _repository.GetRecordAsync(recordKey);
                if (record != null)
                {
                    CurrentRecord = record;
                    BreadcrumbText = _repository.GetBreadcrumb(record.BookKey, record.Reference);
                    ChapterHeader = _repository.GetCanonicalChapterHeader(record.BookKey, record.Reference);
                    await LoadUserDataAsync(recordKey);

                    if (IsContinuousChapter)
                    {
                        await LoadChapterRecordsAsync(recordKey);
                    }

                    // Fire-and-observed (not fire-and-forget): RecordHistorySafeAsync
                    // catches everything internally, so this discarded Task can never
                    // fault. Deliberately not awaited - a slow/locked user.db must
                    // never delay showing the record the user asked for. See
                    // MILESTONE_5_HISTORY_ARCHITECTURE.md section 6.
                    _ = RecordHistorySafeAsync(recordKey);

                    ContentReady?.Invoke();
                }
                else
                {
                    // "Record unavailable" is the same phrase Bookmarks/Recently Read
                    // already use for a RecordKey that no longer resolves - kept
                    // consistent across the app rather than inventing new wording.
                    ErrorMessage = "Record unavailable.";
                }
            }
            catch (Exception ex)
            {
                // Never surface a raw exception message to the user (could be a
                // SQLite/COM error with internal paths/details) - log the real
                // detail, show a plain, non-technical message.
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to load record '{recordKey}': {ex}");
                ErrorMessage = "This passage couldn't be loaded. Please try again.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        // Raised once CurrentRecord, Notes, AND every highlight collection
        // are all fully populated for the record now being displayed - the
        // one reliable point every navigation path (LoadRecordAsync,
        // SwitchToSingleVerse from Chapter View, Next/Previous, @
        // navigation...) converges on. ReadingPage.xaml.cs subscribes to
        // this, not to CurrentRecord's own PropertyChanged, specifically
        // because CurrentRecord is set well BEFORE highlights finish loading
        // in several of those paths (SwitchToSingleVerse fires
        // LoadUserDataAsync fire-and-forget) - reacting to CurrentRecord
        // directly would push text into the RichEditBoxes correctly but
        // apply stale/empty highlight formatting.
        public event Action? ContentReady;

        private async Task LoadUserDataAsync(string recordKey)
        {
            // IsBookmarkedAsync is a targeted single-row lookup - avoids the
            // full-table GetAllBookmarksAsync() scan this used to run on
            // EVERY verse step regardless of whether the verse was ever
            // bookmarked. The full list is only fetched in the (much rarer)
            // case where this verse actually IS bookmarked and its
            // CollectionId is needed.
            bool isBookmarked = await _userRepository.IsBookmarkedAsync(recordKey);
            IsBookmarked = isBookmarked;

            if (isBookmarked)
            {
                var bookmarks = await _userRepository.GetAllBookmarksAsync();
                var currentBookmark = bookmarks.FirstOrDefault(b => b.RecordKey == recordKey);
                CurrentBookmarkCollectionId = currentBookmark?.CollectionId;
            }
            else
            {
                CurrentBookmarkCollectionId = null;
            }

            // Collections rarely change mid reading-session (they're managed
            // from the Bookmarks page) - only fetch them once per session
            // instead of on every single verse step.
            if (Collections.Count == 0)
            {
                await RefreshCollectionsAsync();
            }
            UpdateCurrentCollectionName();

            Notes.Clear();
            var notes = await _userRepository.GetNotesAsync(recordKey);
            foreach (var note in notes) Notes.Add(note);
            await LoadHighlightsAsync(recordKey);
            ContentReady?.Invoke();
        }

        public async Task RefreshCollectionsAsync()
        {
            try
            {
                var cols = await _userRepository.GetCollectionsAsync();
                Collections.Clear();
                foreach (var c in cols) Collections.Add(c);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to load collections: {ex}");
            }
        }

        public void UpdateCurrentCollectionName()
        {
            if (!IsBookmarked)
            {
                CurrentBookmarkCollectionName = string.Empty;
                return;
            }

            if (string.IsNullOrEmpty(CurrentBookmarkCollectionId))
            {
                CurrentBookmarkCollectionName = "Uncategorized";
            }
            else
            {
                var col = Collections.FirstOrDefault(c => c.Id == CurrentBookmarkCollectionId);
                CurrentBookmarkCollectionName = col?.Name ?? "Uncategorized";
            }
        }

        // Reads current settings (cheap - cached after first call) and computes
        // the derived typography values the XAML binds to directly. Runs on
        // every LoadRecordAsync (fresh page, Next/Previous alike) - see
        // MILESTONE_6_SETTINGS_ARCHITECTURE.md "Live update without a full
        // restart". Focus Mode's persisted default is applied only ONCE per
        // page lifetime (guarded by _focusModeInitializedFromSettings) so that
        // Next/Previous never resets an in-session Focus Mode toggle.
        private async Task ApplyReadingSettingsAsync()
        {
            AppSettings settings;
            try
            {
                settings = await _settingsService.GetSettingsAsync();
            }
            catch (Exception ex)
            {
                // Settings read failed (e.g. user.db unavailable) - fall back to
                // the documented defaults rather than leaving stale/blocking the
                // record from loading. Never crash over a preferences read.
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to load reading settings: {ex.Message}");
                settings = new AppSettings();
            }

            double fontScale = settings.FontSize switch
            {
                ReadingFontSize.Small => 0.85,
                ReadingFontSize.Large => 1.15,
                ReadingFontSize.ExtraLarge => 1.30,
                _ => 1.00 // Medium
            };
            double spacingMultiplier = settings.LineSpacing switch
            {
                ReadingLineSpacing.Compact => 1.3,
                ReadingLineSpacing.Relaxed => 1.75,
                _ => 1.5 // Comfortable
            };

            _cachedFontScale = fontScale;
            _cachedSpacingMultiplier = spacingMultiplier;
            ApplyFontSizes(); // no Devanagari LineHeight override - see architecture doc

            ContentMaxWidth = settings.ReadingWidth switch
            {
                ReadingWidthOption.Narrow => 580,
                ReadingWidthOption.Wide => 880,
                _ => 720 // Comfortable (~65-75 chars per line)
            };

            if (!_focusModeInitializedFromSettings)
            {
                IsFocusMode = settings.FocusModeEnabled;
                _focusModeInitializedFromSettings = true;
            }

            ShowTransliteration = settings.ShowTransliteration;
            ShowSynonyms = settings.ShowSynonyms;
            ShowPurport = settings.ShowPurport;
            ShowPronunciationGuide = settings.ShowPronunciationGuide;
        }

        // Recomputes the five FontSize/LineHeight properties from the last
        // settings-derived base (_cachedFontScale/_cachedSpacingMultiplier)
        // and the current ZoomMultiplier. Purely synchronous property
        // writes - no I/O, no CurrentRecord/Notes/Highlights touched - so
        // ReadingPage's RichEditBoxes just re-lay-out their EXISTING content
        // at the new size via ordinary WinUI FontSize binding, instantly and
        // without any flicker, SetText rebuild, or scroll-position reset.
        private void ApplyFontSizes()
        {
            DevanagariFontSize = 24 * _cachedFontScale * ZoomMultiplier;
            TransliterationFontSize = 18 * _cachedFontScale * ZoomMultiplier;
            TransliterationLineHeight = TransliterationFontSize * _cachedSpacingMultiplier;
            SynonymsFontSize = 16 * _cachedFontScale * ZoomMultiplier;
            SynonymsLineHeight = SynonymsFontSize * _cachedSpacingMultiplier;
            TranslationFontSize = 18 * _cachedFontScale * ZoomMultiplier;
            TranslationLineHeight = TranslationFontSize * _cachedSpacingMultiplier;
            PurportFontSize = 18 * _cachedFontScale * ZoomMultiplier;
            PurportLineHeight = PurportFontSize * _cachedSpacingMultiplier;
        }

        [RelayCommand]
        public void ZoomIn()
        {
            ZoomMultiplier = Math.Round(Math.Min(ZoomMax, ZoomMultiplier + ZoomStep), 2);
            ApplyFontSizes();
        }

        [RelayCommand]
        public void ZoomOut()
        {
            ZoomMultiplier = Math.Round(Math.Max(ZoomMin, ZoomMultiplier - ZoomStep), 2);
            ApplyFontSizes();
        }

        [RelayCommand]
        public void ResetZoom()
        {
            ZoomMultiplier = 1.0;
            ApplyFontSizes();
        }

        [RelayCommand]
        public void ToggleFocusMode()
        {
            IsFocusMode = !IsFocusMode;
            // MainPage owns the NavigationView pane - this is the one piece of
            // Focus Mode that needs cross-page communication. See architecture doc.
            WeakReferenceMessenger.Default.Send(new FocusModeChangedMessage(IsFocusMode));
            // Best-effort persistence of the new default - same safe, non-blocking
            // pattern as RecordHistorySafeAsync below. A failure here loses only a
            // convenience default, never existing data.
            _ = PersistFocusModeSafeAsync(IsFocusMode);
        }

        private async Task PersistFocusModeSafeAsync(bool enabled)
        {
            try
            {
                await _settingsService.SetFocusModeAsync(enabled);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to persist Focus Mode default: {ex.Message}");
            }
        }

        // Best-effort history write. A failure here means one visit isn't logged -
        // it never touches, loses, or corrupts existing Bookmarks/Notes/History data,
        // so it is safe to log-and-swallow rather than surface to the user.
        private async Task RecordHistorySafeAsync(string recordKey)
        {
            try
            {
                await _userRepository.RecordHistoryAsync(recordKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadingHistory] Failed to record history for '{recordKey}': {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task ToggleViewModeAsync()
        {
            IsContinuousChapter = !IsContinuousChapter;
            if (IsContinuousChapter && (ChapterRecords.Count == 0 || !ChapterRecords.Any(r => r.RecordKey == CurrentRecord?.RecordKey)))
            {
                if (!string.IsNullOrEmpty(CurrentRecord?.RecordKey))
                {
                    await LoadChapterRecordsAsync(CurrentRecord.RecordKey);
                }
            }
            ContentReady?.Invoke();
        }

        [RelayCommand]
        public void SwitchToSingleVerse(CorpusRecord? record)
        {
            if (record == null) return;
            CurrentRecord = record;
            BreadcrumbText = _repository.GetBreadcrumb(record.BookKey, record.Reference);
            ChapterHeader = _repository.GetCanonicalChapterHeader(record.BookKey, record.Reference);
            IsContinuousChapter = false;
            _ = LoadUserDataAsync(record.RecordKey);
            _ = RecordHistorySafeAsync(record.RecordKey);
            ContentReady?.Invoke();
        }

        public async Task LoadChapterRecordsAsync(string recordKey)
        {
            IsLoadingChapter = true;
            try
            {
                var records = await _repository.GetChapterRecordsAsync(recordKey);
                ChapterRecords.Clear();
                foreach (var r in records) ChapterRecords.Add(r);

                ChapterHeader = _repository.GetCanonicalChapterHeader(CurrentRecord?.BookKey ?? "", CurrentRecord?.Reference);
                ChapterSubtitle = $"{records.Count} Verses • Complete Chapter";
                ContentReady?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to load chapter records: {ex}");
            }
            finally
            {
                IsLoadingChapter = false;
            }
        }

        [RelayCommand]
        public async Task ToggleBookmarkAsync()
        {
            if (string.IsNullOrEmpty(CurrentRecord?.RecordKey)) return;

            bool wasBookmarked = IsBookmarked;
            try
            {
                if (wasBookmarked)
                {
                    await _userRepository.RemoveBookmarkAsync(CurrentRecord.RecordKey);
                    IsBookmarked = false;
                    CurrentBookmarkCollectionId = null;
                    UpdateCurrentCollectionName();
                }
                else
                {
                    await _userRepository.AddBookmarkAsync(CurrentRecord.RecordKey);
                    IsBookmarked = true;
                    CurrentBookmarkCollectionId = null;
                    UpdateCurrentCollectionName();
                }
            }
            catch (Exception ex)
            {
                // Leave IsBookmarked exactly as it was before the attempt - never
                // report success (via a flipped toggle) for a write that failed.
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to toggle bookmark for '{CurrentRecord.RecordKey}': {ex}");
                ErrorMessage = "Couldn't update your bookmark. Please try again.";
            }
        }

        [RelayCommand]
        public async Task SetCollectionAsync(string? collectionId)
        {
            if (string.IsNullOrEmpty(CurrentRecord?.RecordKey)) return;
            try
            {
                if (!IsBookmarked)
                {
                    await _userRepository.AddBookmarkAsync(CurrentRecord.RecordKey);
                    IsBookmarked = true;
                }
                await _userRepository.SetBookmarkCollectionAsync(CurrentRecord.RecordKey, collectionId);
                CurrentBookmarkCollectionId = collectionId;
                UpdateCurrentCollectionName();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to set collection: {ex}");
                ErrorMessage = "Couldn't assign collection. Please try again.";
            }
        }

        [RelayCommand]
        public async Task CreateCollectionAndAssignAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(CurrentRecord?.RecordKey)) return;
            try
            {
                var created = await _userRepository.CreateCollectionAsync(name);
                Collections.Add(created);
                await SetCollectionAsync(created.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to create and assign collection: {ex}");
                ErrorMessage = "Couldn't create collection. Please try again.";
            }
        }

        [RelayCommand]
        public async Task AddNoteAsync()
        {
            if (string.IsNullOrWhiteSpace(NewNoteContent) || string.IsNullOrEmpty(CurrentRecord?.RecordKey)) return;

            string? field = _pendingNoteField;
            int start = _pendingNoteStartOffset;
            int length = _pendingNoteLength;

            try
            {
                var note = await _userRepository.CreateNoteAsync(CurrentRecord.RecordKey, NewNoteContent, field: field, startOffset: start, length: length);
                Notes.Add(note);
                NewNoteContent = string.Empty;
                _pendingNoteField = null;
                _pendingNoteStartOffset = -1;
                _pendingNoteLength = -1;
            }
            catch (Exception ex)
            {
                // Deliberately leave NewNoteContent (and the pending anchor)
                // untouched so the user's typed text isn't lost - they can
                // retry without retyping or re-selecting.
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to save note for '{CurrentRecord.RecordKey}': {ex}");
                ErrorMessage = "Couldn't save your note. Please try again.";
            }
        }

        public async Task<List<UserNote>> GetNotesForRecordAsync(string recordKey)
        {
            try
            {
                return await _userRepository.GetNotesAsync(recordKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to get notes for '{recordKey}': {ex}");
                return new List<UserNote>();
            }
        }

        public async Task<List<UserNote>> GetNotesForChapterAsync(IEnumerable<string> recordKeys)
        {
            var result = new List<UserNote>();
            try
            {
                foreach (var rk in recordKeys)
                {
                    var notes = await _userRepository.GetNotesAsync(rk);
                    result.AddRange(notes);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to get chapter notes: {ex}");
            }
            return result;
        }

        public async Task<UserNote?> SaveOrUpdateNoteAsync(string? noteId, string recordKey, string content, string? title)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrEmpty(recordKey)) return null;
            try
            {
                if (!string.IsNullOrEmpty(noteId))
                {
                    await _userRepository.UpdateNoteAsync(noteId, content, title);
                    var existing = Notes.FirstOrDefault(n => n.Id == noteId);
                    if (existing != null)
                    {
                        existing.Content = content;
                        existing.Title = title;
                        existing.UpdatedUtc = DateTime.UtcNow;
                    }
                    return existing ?? new UserNote { Id = noteId, RecordKey = recordKey, Content = content, Title = title };
                }
                else
                {
                    var note = await _userRepository.CreateNoteAsync(recordKey, content, title: title);
                    Notes.Add(note);
                    return note;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to save/update note: {ex}");
                return null;
            }
        }

        public async Task<bool> DeleteNoteByIdAsync(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return false;
            try
            {
                await _userRepository.DeleteNoteAsync(noteId);
                var existing = Notes.FirstOrDefault(n => n.Id == noteId);
                if (existing != null) Notes.Remove(existing);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to delete note '{noteId}': {ex}");
                return false;
            }
        }

        [RelayCommand]
        public async Task DeleteNoteAsync(UserNote note)
        {
            if (note == null) return;
            try
            {
                await _userRepository.DeleteNoteAsync(note.Id);
                Notes.Remove(note);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to delete note '{note.Id}': {ex}");
                ErrorMessage = "Couldn't delete that note. Please try again.";
            }
        }

        [RelayCommand]
        public async Task GoNextAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentRecord?.RecordKey)) return;
            try
            {
                var nextKey = await _repository.GetAdjacentRecordKeyAsync(CurrentRecord.RecordKey, true);
                if (nextKey != null)
                {
                    await LoadRecordAsync(nextKey);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to find the next record after '{CurrentRecord.RecordKey}': {ex}");
                ErrorMessage = "Couldn't move to the next passage. Please try again.";
            }
        }

        [RelayCommand]
        public async Task GoPreviousAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentRecord?.RecordKey)) return;
            try
            {
                var prevKey = await _repository.GetAdjacentRecordKeyAsync(CurrentRecord.RecordKey, false);
                if (prevKey != null)
                {
                    await LoadRecordAsync(prevKey);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to find the previous record before '{CurrentRecord.RecordKey}': {ex}");
                ErrorMessage = "Couldn't move to the previous passage. Please try again.";
            }
        }

        // Chapter-level navigation (Phase 4.5, Advanced Chapter View only).
        // Reuses the same canonical Sequence-ordered GetAdjacentRecordKeyAsync
        // that GoNext/GoPreviousAsync already rely on - never a fragile UI
        // index - by stepping past the LAST/FIRST record of the currently
        // loaded chapter. Chapters are contiguous in Sequence within a book by
        // construction, so "one past the chapter's last verse" is always the
        // next chapter's first verse (and symmetrically for Previous).
        // LoadRecordAsync already re-loads the full chapter when
        // IsContinuousChapter is true, so no extra chapter-loading logic is
        // needed here.
        [RelayCommand]
        public async Task GoNextChapterAsync()
        {
            if (!IsContinuousChapter || ChapterRecords.Count == 0) return;
            try
            {
                var lastKey = ChapterRecords[ChapterRecords.Count - 1].RecordKey;
                var nextKey = await _repository.GetAdjacentRecordKeyAsync(lastKey, true);
                if (nextKey != null)
                {
                    await LoadRecordAsync(nextKey);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to find the next chapter after '{ChapterHeader}': {ex}");
                ErrorMessage = "Couldn't move to the next chapter. Please try again.";
            }
        }

        [RelayCommand]
        public async Task GoPreviousChapterAsync()
        {
            if (!IsContinuousChapter || ChapterRecords.Count == 0) return;
            try
            {
                var firstKey = ChapterRecords[0].RecordKey;
                var prevKey = await _repository.GetAdjacentRecordKeyAsync(firstKey, false);
                if (prevKey != null)
                {
                    await LoadRecordAsync(prevKey);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reading] Failed to find the previous chapter before '{ChapterHeader}': {ex}");
                ErrorMessage = "Couldn't move to the previous chapter. Please try again.";
            }
        }
    }

    public class BreadcrumbSegment
    {
        public string Text { get; set; } = string.Empty;
        public int Index { get; set; }
        public int TotalSegments { get; set; }
        public bool ShowChevron { get; set; }
        public bool IsLast => Index == TotalSegments - 1;
        public Visibility ChevronVisibility => ShowChevron ? Visibility.Visible : Visibility.Collapsed;
    }
}
