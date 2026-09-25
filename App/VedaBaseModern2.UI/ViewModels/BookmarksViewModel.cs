using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.UI.ViewModels
{
    /// <summary>
    /// One bookmark row, with the collection it currently belongs to attached
    /// so the UI can offer "move to..." without a second round-trip.
    /// </summary>
    public class BookmarkItem
    {
        public string RecordKey { get; set; } = string.Empty;
        public string BookKey { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string? CollectionId { get; set; }
    }

    /// <summary>
    /// One section in the grouped Bookmarks display. Section == null CollectionId
    /// is the always-present "Uncategorized" group for bookmarks not yet filed
    /// into a collection - it is never itself a real BookmarkCollection row.
    /// </summary>
    public partial class BookmarkGroup : ObservableObject
    {
        public string? CollectionId { get; }
        public string Name { get; }
        public bool IsUncategorized => CollectionId == null;
        // Rename/delete only make sense for a real collection, never for the
        // synthetic "Uncategorized" bucket. Fixed at construction (CollectionId
        // never changes after) - a plain property, not a function call, is
        // enough here too.
        public Visibility ManagementButtonsVisibility => IsUncategorized ? Visibility.Collapsed : Visibility.Visible;
        public ObservableCollection<BookmarkItem> Items { get; } = new();

        public Visibility EmptyItemsVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HasItemsVisibility => Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        [ObservableProperty] private Visibility _groupVisibility = Visibility.Visible;

        public BookmarkGroup(string? collectionId, string name)
        {
            CollectionId = collectionId;
            Name = name;
        }
    }

    /// <summary>
    /// Phase 4.9.1: one entry in the Bookmarks page's collection filter picker.
    /// Three shapes: the synthetic "All Bookmarks" (Kind=All), the synthetic
    /// "Uncategorized" bucket (Kind=Uncategorized, CollectionId always null,
    /// same bucket BookmarkGroup already models), and a real user collection
    /// (Kind=Collection, CollectionId set). Rename/Delete only make sense for
    /// the last shape.
    /// </summary>
    public enum CollectionFilterKind { All, Uncategorized, Collection }

    public class CollectionFilterOption
    {
        public CollectionFilterKind Kind { get; init; }
        public string? CollectionId { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool IsRealCollection => Kind == CollectionFilterKind.Collection;
    }

    public partial class BookmarksViewModel : ObservableObject
    {
        private readonly ICorpusRepository _corpusRepository;
        private readonly IUserRepository _userRepository;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusText = "Loading bookmarks...";

        public ObservableCollection<BookmarkGroup> Groups { get; } = new();

        // Every collection, including empty ones - used to populate "move to
        // collection" pickers even before a bookmark has been filed anywhere.
        public ObservableCollection<BookmarkCollection> Collections { get; } = new();

        public bool HasAnyBookmarks => Groups.Any(g => g.Items.Count > 0);
        public bool HasAnyBookmarksOrCollections => Groups.Any(g => g.Items.Count > 0) || Collections.Count > 0;

        // Phase 4.9.1: collection filter. FilterOptions always starts with "All
        // Bookmarks", then "Uncategorized", then every real collection in
        // display order - the same order the old per-collection sections used,
        // just picked from a dropdown instead of scrolling past every section.
        public ObservableCollection<CollectionFilterOption> FilterOptions { get; } = new();
        [ObservableProperty] private CollectionFilterOption? _selectedFilter;
        public ObservableCollection<BookmarkItem> FilteredItems { get; } = new();
        [ObservableProperty] private string _filteredCountText = string.Empty;
        [ObservableProperty] private bool _isFilterEmpty;
        [ObservableProperty] private string _filterEmptyMessage = string.Empty;

        public BookmarksViewModel(ICorpusRepository corpusRepository, IUserRepository userRepository)
        {
            _corpusRepository = corpusRepository;
            _userRepository = userRepository;
        }

        partial void OnSelectedFilterChanged(CollectionFilterOption? value) => ApplyFilter();

        // Recomputes FilteredItems (and the surrounding "N bookmarks" / empty
        // state) from the currently loaded Groups + SelectedFilter. Pure
        // client-side view filtering - never touches the database, never
        // mutates a bookmark's actual CollectionId.
        private void ApplyFilter()
        {
            FilteredItems.Clear();

            IEnumerable<BookmarkItem> source = SelectedFilter?.Kind switch
            {
                CollectionFilterKind.Uncategorized => Groups.FirstOrDefault(g => g.IsUncategorized)?.Items ?? Enumerable.Empty<BookmarkItem>(),
                CollectionFilterKind.Collection => Groups.FirstOrDefault(g => g.CollectionId == SelectedFilter!.CollectionId)?.Items ?? Enumerable.Empty<BookmarkItem>(),
                _ => Groups.SelectMany(g => g.Items),
            };

            foreach (var item in source) FilteredItems.Add(item);

            FilteredCountText = FilteredItems.Count == 1 ? "1 bookmark" : $"{FilteredItems.Count} bookmarks";
            IsFilterEmpty = FilteredItems.Count == 0;
            FilterEmptyMessage = SelectedFilter?.Kind == CollectionFilterKind.All
                ? "No bookmarks yet."
                : $"No bookmarks in “{SelectedFilter?.Name}” yet.";
        }

        public async Task LoadBookmarksAsync()
        {
            IsLoading = true;
            // Remember what was selected so a reload (after rename/delete/move)
            // can restore the same filter instead of silently snapping back to
            // "All Bookmarks" under the user.
            var previousFilterKind = SelectedFilter?.Kind;
            var previousFilterCollectionId = SelectedFilter?.CollectionId;

            Groups.Clear();
            Collections.Clear();
            FilterOptions.Clear();

            try
            {
                var collections = await _userRepository.GetCollectionsAsync();
                var bookmarks = await _userRepository.GetAllBookmarksAsync();

                foreach (var c in collections) Collections.Add(c);

                var groupsById = new Dictionary<string, BookmarkGroup>();
                foreach (var c in collections)
                {
                    var g = new BookmarkGroup(c.Id, c.Name);
                    groupsById[c.Id] = g;
                    Groups.Add(g);
                }
                // Uncategorized always shown last, always present (even with 0 items)
                // so "move here" always has a visible destination.
                var uncategorized = new BookmarkGroup(null, "Uncategorized");
                Groups.Add(uncategorized);

                StatusText = bookmarks.Count == 0 ? "No bookmarks yet." : $"{bookmarks.Count} Bookmarks";

                foreach (var ub in bookmarks)
                {
                    var record = await _corpusRepository.GetRecordAsync(ub.RecordKey);
                    var item = record != null
                        ? new BookmarkItem
                        {
                            RecordKey = record.RecordKey,
                            BookKey = record.BookKey ?? "UNKNOWN",
                            Reference = string.IsNullOrWhiteSpace(record.Reference) ? record.RecordKey : record.Reference,
                            BookTitle = _corpusRepository.GetBookTitle(record.BookKey ?? "UNKNOWN"),
                            Preview = "Bookmarked on " + ub.CreatedUtc.ToLocalTime().ToString("d"),
                            CollectionId = ub.CollectionId
                        }
                        : new BookmarkItem
                        {
                            RecordKey = ub.RecordKey,
                            BookKey = "UNKNOWN",
                            Reference = "Record unavailable",
                            Preview = "This record is no longer available in the corpus.",
                            CollectionId = ub.CollectionId
                        };

                    var targetGroup = (item.CollectionId != null && groupsById.TryGetValue(item.CollectionId, out var g2))
                        ? g2
                        : uncategorized;
                    targetGroup.Items.Add(item);
                }

                // Phase 4.9: All collection groups remain visible so newly created
                // collections appear immediately and can be managed (renamed/deleted).
                foreach (var g in Groups)
                {
                    g.GroupVisibility = Visibility.Visible;
                }

                // Phase 4.9.1: build the filter picker - "All Bookmarks" first,
                // then "Uncategorized", then every real collection in the same
                // order the sections used to appear in.
                FilterOptions.Add(new CollectionFilterOption { Kind = CollectionFilterKind.All, CollectionId = null, Name = "All Bookmarks" });
                FilterOptions.Add(new CollectionFilterOption { Kind = CollectionFilterKind.Uncategorized, CollectionId = null, Name = "Uncategorized" });
                foreach (var c in collections)
                {
                    FilterOptions.Add(new CollectionFilterOption { Kind = CollectionFilterKind.Collection, CollectionId = c.Id, Name = c.Name });
                }

                // Restore whatever was selected before this reload (e.g. after a
                // rename or a move) if it still exists; otherwise fall back to
                // "All Bookmarks" (a deleted collection, or the very first load).
                SelectedFilter = previousFilterKind switch
                {
                    CollectionFilterKind.Collection => FilterOptions.FirstOrDefault(f => f.Kind == CollectionFilterKind.Collection && f.CollectionId == previousFilterCollectionId)
                        ?? FilterOptions[0],
                    CollectionFilterKind.Uncategorized => FilterOptions[1],
                    _ => FilterOptions[0],
                };
                // SelectedFilter's setter only fires OnSelectedFilterChanged (and
                // therefore ApplyFilter) when the new value differs by reference
                // from the old one - which it always does after Clear() rebuilt
                // FilterOptions from scratch, but call explicitly too so the very
                // first load (no previous selection change) still filters.
                ApplyFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Bookmarks] Failed to load bookmarks: {ex}");
                Groups.Clear();
                StatusText = "Couldn't load your bookmarks. Please try again.";
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasAnyBookmarks));
                OnPropertyChanged(nameof(HasAnyBookmarksOrCollections));
            }
        }

        [RelayCommand]
        public async Task CreateCollectionAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                await _userRepository.CreateCollectionAsync(name);
                await LoadBookmarksAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Bookmarks] Failed to create collection: {ex}");
                StatusText = "Couldn't create that collection. Please try again.";
            }
        }

        public async Task RenameCollectionAsync(string collectionId, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            try
            {
                await _userRepository.RenameCollectionAsync(collectionId, newName);
                await LoadBookmarksAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Bookmarks] Failed to rename collection: {ex}");
                StatusText = "Couldn't rename that collection. Please try again.";
            }
        }

        public async Task DeleteCollectionAsync(string collectionId)
        {
            try
            {
                // Bookmarks in the collection are preserved (moved to
                // Uncategorized) by the repository - never deleted here.
                await _userRepository.DeleteCollectionAsync(collectionId);
                await LoadBookmarksAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Bookmarks] Failed to delete collection: {ex}");
                StatusText = "Couldn't delete that collection. Please try again.";
            }
        }

        public async Task MoveBookmarkAsync(string recordKey, string? collectionId)
        {
            try
            {
                await _userRepository.SetBookmarkCollectionAsync(recordKey, collectionId);
                await LoadBookmarksAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Bookmarks] Failed to move bookmark: {ex}");
                StatusText = "Couldn't move that bookmark. Please try again.";
            }
        }

        public async Task RemoveBookmarkAsync(string recordKey)
        {
            try
            {
                await _userRepository.RemoveBookmarkAsync(recordKey);
                await LoadBookmarksAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Bookmarks] Failed to remove bookmark: {ex}");
                StatusText = "Couldn't remove that bookmark. Please try again.";
            }
        }
    }
}
