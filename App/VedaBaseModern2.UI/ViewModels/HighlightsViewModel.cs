using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.UI.ViewModels
{
    /// <summary>
    /// One highlight in the "all highlights" Personal Workspace list, with
    /// enough corpus context to display - per the product brief's own
    /// example ("BG 2.20 '...the soul is never born...' [Yellow]") - and to
    /// navigate back to its exact source passage.
    /// </summary>
    public class HighlightListItem
    {
        public string Id { get; set; } = string.Empty;
        public string RecordKey { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public HighlightColor Color { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string BookKey { get; set; } = string.Empty;
        public bool IsLegacyBlockLevel { get; set; }
        public int StartOffset { get; set; } = -1;
        public int Length { get; set; } = -1;
        public string ColorDisplayName => Color switch
        {
            HighlightColor.Yellow => "Saffron", // Phase 4.9.4: legacy identifier, new display name/color
            HighlightColor.Green => "Green",
            HighlightColor.Blue => "Blue",
            _ => Color.ToString()
        };
        // The exact highlighted phrase for a precision highlight; for a
        // legacy (Phase 4.6, whole-block) highlight there is no captured
        // phrase, so the block name is shown instead - never invented text.
        public string SnippetDisplay { get; set; } = string.Empty;
        public string CreatedDisplay => CreatedUtc.ToLocalTime().ToString("g");

        public string FieldDisplay => Field switch
        {
            "Transliteration" => "Transliteration",
            "Translation" => "Translation",
            "Synonyms" => "Synonyms",
            "Devanagari" => "Devanagari",
            var f when f.StartsWith("Purport:") => $"Purport (¶{f.Substring(8)})",
            "Purport" => "Purport",
            _ => Field
        };
    }

    /// <summary>
    /// Phase 4.9.4: one entry in the Highlights page's scripture filter
    /// picker - mirrors CollectionFilterOption from the Phase 4.9.1 Bookmarks
    /// filter for visual/behavioral consistency. Dynamically populated from
    /// the user's actual highlight data (only books that actually have a
    /// highlight appear), never a hardcoded book list.
    /// </summary>
    public class ScriptureFilterOption
    {
        public bool IsAll { get; init; }
        public string BookKey { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// One entry in the color filter picker. Wraps the real, persisted
    /// HighlightColor enum (never a display-name string) so filtering can
    /// never depend on what a color happens to be labeled today.
    /// </summary>
    public class ColorFilterOption
    {
        public bool IsAll { get; init; }
        public HighlightColor? Color { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    public partial class HighlightsViewModel : ObservableObject
    {
        private readonly ICorpusRepository _corpusRepository;
        private readonly IUserRepository _userRepository;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusText = "Loading highlights...";

        // Unfiltered - kept only so filters can be recomputed client-side
        // without another database round trip.
        public ObservableCollection<HighlightListItem> Highlights { get; } = new();

        // Phase 4.9.4: filter bar state. Combining both filters (Part 4) is
        // just ApplyFilter ANDing two predicates together - see below.
        public ObservableCollection<ScriptureFilterOption> ScriptureFilterOptions { get; } = new();
        public ObservableCollection<ColorFilterOption> ColorFilterOptions { get; } = new();
        [ObservableProperty] private ScriptureFilterOption? _selectedScriptureFilter;
        [ObservableProperty] private ColorFilterOption? _selectedColorFilter;
        [ObservableProperty] private string _searchText = string.Empty;
        public ObservableCollection<HighlightListItem> FilteredHighlights { get; } = new();
        [ObservableProperty] private string _filteredCountText = string.Empty;
        [ObservableProperty] private bool _isFilterEmpty;

        public HighlightsViewModel(ICorpusRepository corpusRepository, IUserRepository userRepository)
        {
            _corpusRepository = corpusRepository;
            _userRepository = userRepository;
        }

        partial void OnSelectedScriptureFilterChanged(ScriptureFilterOption? value)
        {
            if (!IsLoading) ApplyFilter();
        }

        partial void OnSelectedColorFilterChanged(ColorFilterOption? value)
        {
            if (!IsLoading) ApplyFilter();
        }

        partial void OnSearchTextChanged(string value)
        {
            if (!IsLoading) ApplyFilter();
        }

        // Pure client-side view filtering (Part 4: "Filtering must happen in
        // the ViewModel/query layer... must never modify user data") - never
        // touches the database, never mutates a Highlight record.
        private void ApplyFilter()
        {
            FilteredHighlights.Clear();

            IEnumerable<HighlightListItem> filtered = Highlights;
            if (SelectedScriptureFilter != null && !SelectedScriptureFilter.IsAll)
            {
                filtered = filtered.Where(h => h.BookKey == SelectedScriptureFilter.BookKey);
            }
            if (SelectedColorFilter != null && !SelectedColorFilter.IsAll && SelectedColorFilter.Color.HasValue)
            {
                var c = SelectedColorFilter.Color.Value;
                filtered = filtered.Where(h => h.Color == c);
            }
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var query = SearchText.Trim();
                filtered = filtered.Where(h =>
                    (h.SnippetDisplay != null && h.SnippetDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (h.BookTitle != null && h.BookTitle.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (h.Reference != null && h.Reference.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (h.Field != null && h.Field.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (h.ColorDisplayName != null && h.ColorDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var h in filtered) FilteredHighlights.Add(h);

            FilteredCountText = FilteredHighlights.Count == 1 ? "1 highlight" : $"{FilteredHighlights.Count} highlights";
            IsFilterEmpty = FilteredHighlights.Count == 0;
        }

        private static readonly (HighlightColor Color, string Name)[] ColorNames =
        {
            (HighlightColor.Yellow, "Saffron"), // legacy identifier, new display name (Phase 4.9.4)
            (HighlightColor.Green, "Green"),
            (HighlightColor.Blue, "Blue"),
        };

        public async Task LoadHighlightsAsync()
        {
            IsLoading = true;
            // Preserve the user's current filter selection across a reload
            // (e.g. after removing a highlight) where possible.
            string? prevBookKey = SelectedScriptureFilter is { IsAll: false } sf ? sf.BookKey : null;
            HighlightColor? prevColor = SelectedColorFilter is { IsAll: false } cf ? cf.Color : null;

            Highlights.Clear();
            ScriptureFilterOptions.Clear();
            ColorFilterOptions.Clear();
            try
            {
                var all = await _userRepository.GetAllHighlightsAsync();
                if (all.Count == 0)
                {
                    StatusText = "No highlights yet.";
                    return;
                }
                StatusText = $"{all.Count} Highlights";

                var booksSeen = new Dictionary<string, string>(); // BookKey -> BookTitle, insertion order
                var recordCache = new Dictionary<string, CorpusRecord?>();

                foreach (var h in all)
                {
                    if (!recordCache.TryGetValue(h.RecordKey, out var record))
                    {
                        record = await _corpusRepository.GetRecordAsync(h.RecordKey);
                        recordCache[h.RecordKey] = record;
                    }
                    string bookKey = record?.BookKey ?? string.Empty;
                    string bookTitle = record != null ? _corpusRepository.GetBookTitle(bookKey) : "Record unavailable";

                    if (!string.IsNullOrEmpty(bookKey) && !booksSeen.ContainsKey(bookKey))
                    {
                        booksSeen[bookKey] = bookTitle;
                    }

                    Highlights.Add(new HighlightListItem
                    {
                        Id = h.Id,
                        RecordKey = h.RecordKey,
                        Field = h.Field,
                        Color = h.Color,
                        CreatedUtc = h.CreatedUtc,
                        IsLegacyBlockLevel = h.IsLegacyBlockLevel,
                        StartOffset = h.StartOffset,
                        Length = h.Length,
                        SnippetDisplay = h.IsLegacyBlockLevel
                            ? $"[{h.Field} - whole section highlighted]"
                            : $"“{Truncate(h.SelectedText)}”",
                        Reference = record != null
                            ? (string.IsNullOrWhiteSpace(record.Reference) ? record.RecordKey : record.Reference)
                            : h.RecordKey,
                        BookTitle = bookTitle,
                        BookKey = bookKey
                    });
                }

                // Part 2: "Only display scriptures that actually have
                // highlights" - booksSeen was built purely from this user's
                // own highlight data, in the order first encountered.
                ScriptureFilterOptions.Add(new ScriptureFilterOption { IsAll = true, Name = "All Scriptures" });
                foreach (var (bookKey, bookTitle) in booksSeen)
                {
                    ScriptureFilterOptions.Add(new ScriptureFilterOption { BookKey = bookKey, Name = bookTitle });
                }
                SelectedScriptureFilter = prevBookKey != null
                    ? ScriptureFilterOptions.FirstOrDefault(o => o.BookKey == prevBookKey) ?? ScriptureFilterOptions[0]
                    : ScriptureFilterOptions[0];

                ColorFilterOptions.Add(new ColorFilterOption { IsAll = true, Name = "All Colors" });
                foreach (var (color, name) in ColorNames)
                {
                    ColorFilterOptions.Add(new ColorFilterOption { Color = color, Name = name });
                }
                SelectedColorFilter = prevColor.HasValue
                    ? ColorFilterOptions.FirstOrDefault(o => o.Color == prevColor.Value) ?? ColorFilterOptions[0]
                    : ColorFilterOptions[0];

                ApplyFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Highlights] Failed to load highlights: {ex}");
                Highlights.Clear();
                StatusText = "Couldn't load your highlights. Please try again.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static string Truncate(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            const int max = 80;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public async Task RemoveHighlightAsync(string highlightId)
        {
            try
            {
                await _userRepository.RemoveHighlightAsync(highlightId);
                var item = Highlights.FirstOrDefault(h => h.Id == highlightId);
                if (item != null) Highlights.Remove(item);
                var filteredItem = FilteredHighlights.FirstOrDefault(h => h.Id == highlightId);
                if (filteredItem != null) FilteredHighlights.Remove(filteredItem);

                // If the deleted highlight's book no longer has any highlights, update ScriptureFilterOptions
                if (item != null && !string.IsNullOrEmpty(item.BookKey) && !Highlights.Any(h => h.BookKey == item.BookKey))
                {
                    var opt = ScriptureFilterOptions.FirstOrDefault(o => o.BookKey == item.BookKey);
                    if (opt != null)
                    {
                        if (SelectedScriptureFilter == opt)
                        {
                            SelectedScriptureFilter = ScriptureFilterOptions.FirstOrDefault(o => o.IsAll) ?? ScriptureFilterOptions[0];
                        }
                        ScriptureFilterOptions.Remove(opt);
                    }
                }

                FilteredCountText = FilteredHighlights.Count == 1 ? "1 highlight" : $"{FilteredHighlights.Count} highlights";
                IsFilterEmpty = FilteredHighlights.Count == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Highlights] Failed to remove highlight: {ex}");
                StatusText = "Couldn't remove that highlight. Please try again.";
            }
        }
    }
}
