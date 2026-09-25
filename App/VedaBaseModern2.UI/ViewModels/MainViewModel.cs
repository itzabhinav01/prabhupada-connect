using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ICorpusRepository _repository;
        private readonly IUserRepository _userRepository;

        public static string NormalizeForSorting(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var trimmed = text.Trim().Trim('"', '\'', '“', '”', '‘', '’');
            var normalizedString = trimmed.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(capacity: normalizedString.Length);
            foreach (char c in normalizedString)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        [ObservableProperty]
        private ObservableCollection<BookNode> _books = new();

        // Personal Workspace quick-menu counts (Phase 4.6). Shown only where
        // meaningful - see PersonalWorkspacePanel in MainPage.xaml, which omits
        // the badge entirely for a zero count rather than showing "0".
        [ObservableProperty] private int _bookmarkCount;
        [ObservableProperty] private int _noteCount;
        [ObservableProperty] private int _highlightCount;

        public MainViewModel(ICorpusRepository repository, IUserRepository userRepository)
        {
            _repository = repository;
            _userRepository = userRepository;
        }

        // Cheap COUNT(*) queries - called every time the workspace panel opens
        // so the badges never go stale after a bookmark/note/highlight is
        // added or removed elsewhere in the app.
        public async Task RefreshWorkspaceCountsAsync()
        {
            try
            {
                BookmarkCount = await _userRepository.GetBookmarkCountAsync();
                NoteCount = await _userRepository.GetNoteCountAsync();
                HighlightCount = await _userRepository.GetHighlightCountAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Main] Failed to refresh workspace counts: {ex}");
            }
        }

        public async Task LoadBooksAsync(bool forceReload = false)
        {
            if (Books.Count > 0 && !forceReload) return;
            try
            {
                if (forceReload)
                {
                    _repository.InvalidateLibraryHierarchyCache();
                }

                var books = await _repository.GetLibraryHierarchyAsync();
                var overrides = await _userRepository.GetBookCategoryOverridesAsync();

                BookNode? ccNode = null;
                var ccChapters = new List<ChapterNode>();

                var loadedList = new List<BookNode>();

                foreach (var b in books)
                {
                    if (b.BookKey == "DI" || b.BookKey == "MADHYA" || b.BookKey == "ANTYA")
                    {
                        if (ccNode == null)
                        {
                            ccNode = new BookNode
                            {
                                BookKey = "CC",
                                Title = "Śrī Caitanya-caritāmṛta",
                                Chapters = ccChapters
                            };
                            loadedList.Add(ccNode);
                        }

                        string lilaPrefix = b.BookKey == "DI" ? "Ādi-līlā" : (b.BookKey == "MADHYA" ? "Madhya-līlā" : "Antya-līlā");
                        foreach (var ch in b.Chapters)
                        {
                            bool needsLilaPrefix = ch.Title.StartsWith("Chapter", StringComparison.OrdinalIgnoreCase) ||
                                ch.Title.Equals("Dedication", StringComparison.OrdinalIgnoreCase) ||
                                ch.Title.Equals("Preface", StringComparison.OrdinalIgnoreCase) ||
                                ch.Title.Equals("Introduction", StringComparison.OrdinalIgnoreCase) ||
                                ch.Title.Equals("Foreword", StringComparison.OrdinalIgnoreCase);
                            string cleanChTitle = needsLilaPrefix
                                ? $"{lilaPrefix} {ch.Title}"
                                : ch.Title;
                            ccChapters.Add(new ChapterNode
                            {
                                Title = cleanChTitle,
                                Records = ch.Records
                            });
                        }
                    }
                    else
                    {
                        loadedList.Add(b);
                    }
                }

                var canonicalGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Major Scripture",
                    "Core Summary Study",
                    "Shorter Scripture",
                    "Scripture",
                    "Philosophy",
                    "Foundational Book",
                    "Dialogue",
                    "Anthology",
                    "Manual",
                    "Biographies",
                    "Other Books",
                    "Other Ācāryas"
                };

                // Assign each book to its computed category folder
                var bookFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var b in loadedList)
                {
                    if (overrides.TryGetValue(b.BookKey, out var userCat) &&
                        !string.IsNullOrWhiteSpace(userCat) &&
                        !userCat.Equals("(Default Folder)", StringComparison.OrdinalIgnoreCase))
                    {
                        bookFolders[b.BookKey] = userCat.Trim();
                    }
                    else if (!b.IsOtherAuthor)
                    {
                        bookFolders[b.BookKey] = "ŚRĪLA PRABHUPĀDA'S WORKS";
                    }
                    else if (!string.IsNullOrWhiteSpace(b.Category) && !canonicalGenres.Contains(b.Category.Trim()))
                    {
                        bookFolders[b.BookKey] = b.Category.Trim();
                    }
                    else
                    {
                        bookFolders[b.BookKey] = "WORKS BY OTHER ĀCĀRYAS & AUTHORS";
                    }
                }

                var customOrders = await _userRepository.GetBookDisplayOrderAsync();

                void SortFolderBooks(List<BookNode> bList)
                {
                    bList.Sort((a, b) =>
                    {
                        bool hasA = customOrders.TryGetValue(a.BookKey, out int oa);
                        bool hasB = customOrders.TryGetValue(b.BookKey, out int ob);
                        if (hasA && hasB && oa != ob) return oa.CompareTo(ob);
                        if (hasA && !hasB) return -1;
                        if (!hasA && hasB) return 1;
                        // Default: Alphabetical order (A-Z) by diacritic-normalized Title
                        string normA = NormalizeForSorting(a.Title);
                        string normB = NormalizeForSorting(b.Title);
                        int cmp = string.Compare(normA, normB, StringComparison.CurrentCultureIgnoreCase);
                        return cmp != 0 ? cmp : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
                    });
                }

                var groupedList = new List<BookNode>();

                // 1. ŚRĪLA PRABHUPĀDA'S WORKS (Collapsible Dropdown Folder)
                var spBooks = loadedList.Where(b => bookFolders.TryGetValue(b.BookKey, out var f) && f.Equals("ŚRĪLA PRABHUPĀDA'S WORKS", StringComparison.OrdinalIgnoreCase)).ToList();
                SortFolderBooks(spBooks);

                if (spBooks.Count > 0)
                {
                    groupedList.Add(new BookNode
                    {
                        BookKey = "__FOLDER_SP__",
                        Title = "Śrīla Prabhupāda's Works",
                        IsFolder = true,
                        Children = spBooks
                    });
                }

                // 2. Custom Folders (e.g. Japa Books, Scientific Books, Vaiṣṇava Etiquette, etc.)
                var customFolders = bookFolders.Values
                    .Where(f => !f.Equals("ŚRĪLA PRABHUPĀDA'S WORKS", StringComparison.OrdinalIgnoreCase) &&
                                !f.Equals("WORKS BY OTHER ĀCĀRYAS & AUTHORS", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => f)
                    .ToList();

                foreach (var folder in customFolders)
                {
                    var folderBooks = loadedList.Where(b => bookFolders.TryGetValue(b.BookKey, out var f) && f.Equals(folder, StringComparison.OrdinalIgnoreCase)).ToList();
                    SortFolderBooks(folderBooks);

                    if (folderBooks.Count > 0)
                    {
                        groupedList.Add(new BookNode
                        {
                            BookKey = $"__FOLDER_{folder}__",
                            Title = folder,
                            IsFolder = true,
                            Children = folderBooks
                        });
                    }
                }

                // 3. WORKS BY OTHER ĀCĀRYAS & AUTHORS (Collapsible Dropdown Folder)
                var otherAuthorBooks = loadedList.Where(b => bookFolders.TryGetValue(b.BookKey, out var f) && f.Equals("WORKS BY OTHER ĀCĀRYAS & AUTHORS", StringComparison.OrdinalIgnoreCase)).ToList();
                SortFolderBooks(otherAuthorBooks);

                if (otherAuthorBooks.Count > 0)
                {
                    groupedList.Add(new BookNode
                    {
                        BookKey = "__FOLDER_OTHER__",
                        Title = "Works by Other Ācāryas & Authors",
                        IsFolder = true,
                        Children = otherAuthorBooks
                    });
                }

                // Apply user-defined folder display order if customized
                var savedFolderOrder = await _userRepository.GetFolderDisplayOrderAsync();
                int GetFolderSortPriority(string folderTitle)
                {
                    int idx = savedFolderOrder.FindIndex(f => f.Equals(folderTitle, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) return idx;
                    if (folderTitle.Equals("Śrīla Prabhupāda's Works", StringComparison.OrdinalIgnoreCase)) return -100;
                    if (folderTitle.Equals("Works by Other Ācāryas & Authors", StringComparison.OrdinalIgnoreCase)) return 1000;
                    return 0;
                }

                groupedList.Sort((a, b) => GetFolderSortPriority(a.Title).CompareTo(GetFolderSortPriority(b.Title)));

                Books = new ObservableCollection<BookNode>(groupedList);
            }
            catch (Exception ex)
            {
                // Deliberately does not surface a new UI element for this: the
                // corpus is a static local file, so this is an extremely
                // unlikely failure. Preventing a startup crash is what matters -
                // Bookmarks/Recently Read/Settings (the other static nav items)
                // remain fully usable even if the dynamic book list is empty.
                System.Diagnostics.Debug.WriteLine($"[Main] Failed to load the library book list: {ex}");
            }
        }
    }
}
