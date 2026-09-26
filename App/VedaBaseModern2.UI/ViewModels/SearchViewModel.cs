using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;
using VedaBaseModern.Core.Services;

namespace VedaBaseModern.UI.ViewModels
{
    public partial class SearchViewModel : ObservableObject
    {
        private readonly IUnifiedSearchService _searchService;
        private readonly ICorpusRepository _repository;
        private readonly DirectReferenceService _referenceService;
        private List<SearchResult> _cachedAllResults = new();

        // Guards against a slow suggestion lookup for an earlier keystroke
        // overwriting the (already-newer) suggestions for a later one - see
        // OnSearchTextChanged.
        private int _suggestionRequestVersion;

        // ---- "@" direct-reference mode ----
        [ObservableProperty]
        private bool _isReferenceMode;

        public ObservableCollection<ReferenceSuggestion> ReferenceSuggestions { get; } = new();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _statusText = "Search the VedaBase";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private int _totalResults;

        [ObservableProperty]
        private BookNode? _selectedBookFilter;

        [ObservableProperty]
        private string _selectedBooksSummary = "All Books (A–Z)";

        [ObservableProperty]
        private string _selectedBooksCountText = "All selected";

        [ObservableProperty]
        private string _bookFilterSearchText = string.Empty;

        public ObservableCollection<BookFilterItem> FilterableBooks { get; } = new();
        public ObservableCollection<BookFilterItem> FilteredBookItems { get; } = new();

        [ObservableProperty]
        private int _selectedFacetIndex = 0; // 0 = All, 1 = Scripture, 2 = Notes, 3 = Bookmarks, 4 = Highlights

        [ObservableProperty]
        private string _selectedFieldScope = "All Fields";

        [ObservableProperty]
        private bool _isExactWordMatch = true;

        [ObservableProperty]
        private bool _isExactCaseMatch = true;

        [ObservableProperty]
        private string _selectedSortOption = "Best Match (Relevance)";

        [ObservableProperty]
        private bool _hasMoreResults;

        [ObservableProperty]
        private string _loadMoreButtonText = string.Empty;

        [ObservableProperty]
        private bool _isLoadingMore;

        private int _totalScriptureCount;

        private List<string>? _checkedBookKeys;
        public List<string>? CheckedBookKeys
        {
            get => _checkedBookKeys;
            set
            {
                _checkedBookKeys = value;
                if (FilterableBooks.Count > 0)
                {
                    SyncCheckboxesFromKeys();
                }
            }
        }

        public ObservableCollection<SearchResult> Results { get; } = new();
        public ObservableCollection<BookNode> AvailableBooks { get; } = new();
        public ObservableCollection<string> SortOptions { get; } = new()
        {
            "Best Match (Relevance)",
            "Canonical Order"
        };
        public ObservableCollection<string> FieldScopeOptions { get; } = new()
        {
            "All Fields",
            "Verse & Synonyms",
            "Verse / Transliteration Only",
            "Translations Only",
            "Synonyms Only",
            "Purports Only",
            "Devanagari Only"
        };
        public ObservableCollection<string> FacetOptions { get; } = new()
        {
            "All Results",
            "Scripture",
            "My Notes",
            "Bookmarks",
            "My Highlights"
        };

        public SearchViewModel(IUnifiedSearchService searchService, ICorpusRepository repository, DirectReferenceService referenceService)
        {
            _searchService = searchService;
            _repository = repository;
            _referenceService = referenceService;
            InitializeBooksAsync();
        }

        // Fires on every keystroke (the AutoSuggestBox binds Text TwoWay with
        // UpdateSourceTrigger=PropertyChanged in the XAML). "@..." switches to
        // direct-reference mode and refreshes suggestions from the in-memory
        // index (no SQLite access per keystroke - see DirectReferenceService).
        partial void OnSearchTextChanged(string value)
        {
            if (DirectReferenceService.IsReferenceQuery(value))
            {
                IsReferenceMode = true;
                _ = RefreshReferenceSuggestionsAsync(value);
            }
            else
            {
                IsReferenceMode = false;
                ReferenceSuggestions.Clear();
            }
        }

        private async Task RefreshReferenceSuggestionsAsync(string text)
        {
            int myVersion = ++_suggestionRequestVersion;
            List<ReferenceSuggestion> suggestions;
            try
            {
                suggestions = await _referenceService.GetSuggestionsAsync(text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Search] Reference suggestion lookup failed for '{text}': {ex}");
                return;
            }

            // A newer keystroke already started a fresher lookup - discard
            // this now-stale result rather than let it flash over the current one.
            if (myVersion != _suggestionRequestVersion) return;

            ReferenceSuggestions.Clear();
            foreach (var s in suggestions) ReferenceSuggestions.Add(s);
        }

        /// <summary>
        /// Enter-to-navigate in direct-reference mode: resolves the current
        /// "@..." text to an exact RecordKey, or null if it doesn't (yet)
        /// name a complete, existing verse. Code-behind performs the actual
        /// Frame navigation - this ViewModel never touches UI navigation
        /// directly.
        /// </summary>
        public async Task<string?> TryResolveDirectReferenceAsync()
        {
            if (!IsReferenceMode) return null;
            try
            {
                return await _referenceService.TryResolveExactAsync(SearchText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Search] Direct reference resolution failed for '{SearchText}': {ex}");
                return null;
            }
        }

        private async void InitializeBooksAsync()
        {
            try
            {
                var books = await _repository.GetLibraryHierarchyAsync();

                // Sort books ascending A–Z by Title
                var sortedBooks = books
                    .Where(b => !string.IsNullOrEmpty(b.Title))
                    .OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                AvailableBooks.Clear();
                AvailableBooks.Add(new BookNode { Title = "All Books (A–Z)", BookKey = "" });

                FilterableBooks.Clear();
                FilteredBookItems.Clear();

                foreach (var b in sortedBooks)
                {
                    AvailableBooks.Add(b);

                    var filterItem = new BookFilterItem
                    {
                        BookKey = b.BookKey,
                        Title = b.Title,
                        IsSelected = true
                    };
                    FilterableBooks.Add(filterItem);
                    FilteredBookItems.Add(filterItem);
                }

                SelectedBookFilter = AvailableBooks[0];
                SelectedBooksSummary = "All Books (A–Z)";
                SelectedBooksCountText = "All selected";

                if (CheckedBookKeys != null && CheckedBookKeys.Count > 0)
                {
                    SyncCheckboxesFromKeys();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Search] Failed to load the book filter list: {ex}");
            }
        }

        public void UpdateFilteredBookItems(string searchFilter)
        {
            FilteredBookItems.Clear();
            var filter = (searchFilter ?? "").Trim();
            foreach (var item in FilterableBooks)
            {
                if (string.IsNullOrEmpty(filter) ||
                    item.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                    item.BookKey.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredBookItems.Add(item);
                }
            }
        }

        public void SelectAllBooks(bool selectAll)
        {
            foreach (var item in FilterableBooks)
            {
                item.IsSelected = selectAll;
            }
            UpdateSelectedBooksFromCheckboxes();
        }

        public void UpdateSelectedBooksFromCheckboxes()
        {
            var selected = FilterableBooks.Where(b => b.IsSelected).ToList();
            if (selected.Count == 0 || selected.Count == FilterableBooks.Count)
            {
                CheckedBookKeys = null;
                SelectedBooksSummary = "All Books (A–Z)";
                SelectedBooksCountText = "All selected";
            }
            else if (selected.Count == 1)
            {
                CheckedBookKeys = new List<string> { selected[0].BookKey };
                SelectedBooksSummary = selected[0].Title;
                SelectedBooksCountText = "1 selected";
            }
            else
            {
                CheckedBookKeys = selected.Select(b => b.BookKey).ToList();
                SelectedBooksSummary = $"{selected.Count} Books Selected";
                SelectedBooksCountText = $"{selected.Count} of {FilterableBooks.Count} selected";
            }
        }

        public void SyncCheckboxesFromKeys()
        {
            if (_checkedBookKeys == null || _checkedBookKeys.Count == 0)
            {
                foreach (var b in FilterableBooks) b.IsSelected = true;
                SelectedBooksSummary = "All Books (A–Z)";
                SelectedBooksCountText = "All selected";
                return;
            }

            var keySet = new HashSet<string>(_checkedBookKeys, StringComparer.OrdinalIgnoreCase);
            int count = 0;
            string lastTitle = "";

            foreach (var b in FilterableBooks)
            {
                bool match = keySet.Contains(b.BookKey);
                if (!match && keySet.Contains("CC") && (b.BookKey == "DI" || b.BookKey == "MADHYA" || b.BookKey == "ANTYA"))
                {
                    match = true;
                }
                b.IsSelected = match;
                if (match)
                {
                    count++;
                    lastTitle = b.Title;
                }
            }

            if (count == 0 || count == FilterableBooks.Count)
            {
                SelectedBooksSummary = "All Books (A–Z)";
                SelectedBooksCountText = "All selected";
            }
            else if (count == 1)
            {
                SelectedBooksSummary = lastTitle;
                SelectedBooksCountText = "1 selected";
            }
            else
            {
                SelectedBooksSummary = $"{count} Books Selected";
                SelectedBooksCountText = $"{count} of {FilterableBooks.Count} selected";
            }
        }

        partial void OnSelectedFacetIndexChanged(int value)
        {
            ApplyFacetFilter();
        }

        private void ApplyFacetFilter()
        {
            Results.Clear();
            IEnumerable<SearchResult> filtered = _cachedAllResults;

            if (SelectedFacetIndex == 1) // Scripture
            {
                filtered = _cachedAllResults.Where(r => r.Category == "Scripture");
            }
            else if (SelectedFacetIndex == 2) // Notes
            {
                filtered = _cachedAllResults.Where(r => r.Category == "Note");
            }
            else if (SelectedFacetIndex == 3) // Bookmarks
            {
                filtered = _cachedAllResults.Where(r => r.Category == "Bookmark");
            }
            else if (SelectedFacetIndex == 4) // Highlights
            {
                filtered = _cachedAllResults.Where(r => r.Category == "Highlight");
            }

            foreach (var r in filtered)
            {
                Results.Add(r);
            }

            UpdateHasMore();
        }

        partial void OnSelectedFieldScopeChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                _ = ExecuteSearchAsync();
            }
        }

        partial void OnIsExactWordMatchChanged(bool value)
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                _ = ExecuteSearchAsync();
            }
        }

        partial void OnIsExactCaseMatchChanged(bool value)
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                _ = ExecuteSearchAsync();
            }
        }

        partial void OnSelectedSortOptionChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                _ = ExecuteSearchAsync();
            }
        }

        private void UpdateHasMore()
        {
            int currentCount = Results.Count;
            int scriptureLoaded = _cachedAllResults.Count(r => r.Category == "Scripture");
            HasMoreResults = (SelectedFacetIndex == 0 || SelectedFacetIndex == 1) && scriptureLoaded < _totalScriptureCount;
            LoadMoreButtonText = $"Load More Results (Showing {currentCount} of {TotalResults})";
        }

        [RelayCommand]
        public async Task LoadMoreAsync()
        {
            if (IsLoading || IsLoadingMore || !HasMoreResults) return;

            IsLoadingMore = true;
            try
            {
                var query = SearchText.Trim();
                string? filterKey = (CheckedBookKeys != null && CheckedBookKeys.Count > 0)
                    ? null
                    : (string.IsNullOrEmpty(SelectedBookFilter?.BookKey) ? null : SelectedBookFilter.BookKey);

                string? scope = SelectedFieldScope switch
                {
                    "Purports Only" => "Purports",
                    "Translations Only" => "Translation",
                    "Synonyms Only" => "Synonyms",
                    "Devanagari Only" => "Devanagari",
                    "Verse / Transliteration Only" => "Transliteration",
                    "Verse & Synonyms" => "{Transliteration Synonyms}",
                    _ => null
                };

                string sort = SelectedSortOption?.Contains("Canonical") == true ? "canonical" : "relevance";
                int offset = _cachedAllResults.Count(r => r.Category == "Scripture");
                var moreResults = await _searchService.SearchAsync(query, filterKey, 100, offset, scope, CheckedBookKeys, IsExactWordMatch, sort, IsExactCaseMatch);

                foreach (var r in moreResults.Results)
                {
                    if (!_cachedAllResults.Any(existing => existing.RecordKey == r.RecordKey && existing.Category == r.Category))
                    {
                        _cachedAllResults.Add(r);
                    }
                }

                ApplyFacetFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Search] LoadMore failed: {ex}");
            }
            finally
            {
                IsLoadingMore = false;
            }
        }

        [RelayCommand]
        public async Task ExecuteSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                _cachedAllResults.Clear();
                Results.Clear();
                TotalResults = 0;
                _totalScriptureCount = 0;
                HasMoreResults = false;
                LoadMoreButtonText = string.Empty;
                StatusText = "Search the VedaBase";
                return;
            }

            IsLoading = true;
            StatusText = "Searching...";
            _cachedAllResults.Clear();
            Results.Clear();
            TotalResults = 0;
            _totalScriptureCount = 0;
            HasMoreResults = false;

            try
            {
                var query = SearchText.Trim();
                string? filterKey = (CheckedBookKeys != null && CheckedBookKeys.Count > 0)
                    ? null
                    : (string.IsNullOrEmpty(SelectedBookFilter?.BookKey) ? null : SelectedBookFilter.BookKey);

                string? scope = SelectedFieldScope switch
                {
                    "Purports Only" => "Purports",
                    "Translations Only" => "Translation",
                    "Synonyms Only" => "Synonyms",
                    "Devanagari Only" => "Devanagari",
                    "Verse / Transliteration Only" => "Transliteration",
                    "Verse & Synonyms" => "{Transliteration Synonyms}",
                    _ => null
                };

                string sort = SelectedSortOption?.Contains("Canonical") == true ? "canonical" : "relevance";
                var unifiedResult = await _searchService.SearchAsync(query, filterKey, 100, 0, scope, CheckedBookKeys, IsExactWordMatch, sort, IsExactCaseMatch);

                _cachedAllResults = unifiedResult.Results;
                TotalResults = unifiedResult.TotalCount;
                _totalScriptureCount = unifiedResult.ScriptureCount;

                // Update facet tab labels with live counts
                FacetOptions[0] = $"All ({unifiedResult.TotalCount})";
                FacetOptions[1] = $"Scripture ({unifiedResult.ScriptureCount})";
                FacetOptions[2] = $"My Notes ({unifiedResult.NotesCount})";
                FacetOptions[3] = $"Bookmarks ({unifiedResult.BookmarksCount})";
                FacetOptions[4] = $"My Highlights ({unifiedResult.HighlightsCount})";

                ApplyFacetFilter();

                if (unifiedResult.TotalCount == 0)
                {
                    StatusText = $"No results found for \"{SearchText}\". Try a different word or phrase.";
                }
                else
                {
                    var parts = new List<string>();
                    if (unifiedResult.ScriptureCount > 0) parts.Add($"{unifiedResult.ScriptureCount} scripture");
                    if (unifiedResult.NotesCount > 0) parts.Add($"{unifiedResult.NotesCount} note{(unifiedResult.NotesCount == 1 ? "" : "s")}");
                    if (unifiedResult.BookmarksCount > 0) parts.Add($"{unifiedResult.BookmarksCount} bookmark{(unifiedResult.BookmarksCount == 1 ? "" : "s")}");
                    if (unifiedResult.HighlightsCount > 0) parts.Add($"{unifiedResult.HighlightsCount} highlight{(unifiedResult.HighlightsCount == 1 ? "" : "s")}");

                    StatusText = $"Results for \"{SearchText}\": {string.Join(", ", parts)}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Search] Search failed for query '{SearchText}': {ex}");
                _cachedAllResults.Clear();
                Results.Clear();
                TotalResults = 0;
                _totalScriptureCount = 0;
                HasMoreResults = false;
                StatusText = "Search couldn't complete. Please try again.";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    public partial class BookFilterItem : ObservableObject
    {
        public string BookKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isSelected = true;
    }
}
