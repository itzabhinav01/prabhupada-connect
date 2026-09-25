using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.UI.ViewModels
{
    public class CantoNode
    {
        public int CantoNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public List<ChapterNode> Chapters { get; set; } = new();
        public int TotalVerses => Chapters.Sum(c => c.Records.Count);
        public string VersesCountText => $"{Chapters.Count} chapters • {TotalVerses} verses";
    }

    public partial class LibraryViewModel : ObservableObject
    {
        private static readonly Dictionary<int, string> CantoTitles = new()
        {
            { 1, "Creation" },
            { 2, "The Cosmic Manifestation" },
            { 3, "The Status Quo" },
            { 4, "The Creation of the Fourth Order" },
            { 5, "The Creative Impetus" },
            { 6, "Prescribed Duties for Mankind" },
            { 7, "The Science of God" },
            { 8, "Withdrawal of the Cosmic Creations" },
            { 9, "Liberation" },
            { 10, "The Summum Bonum" },
            { 11, "General History" },
            { 12, "The Age of Deterioration" }
        };

        [ObservableProperty]
        private BookNode _currentBook = new BookNode();

        [ObservableProperty]
        private bool _isSb;

        [ObservableProperty]
        private bool _isCc;

        [ObservableProperty]
        private bool _hasLevel0;

        [ObservableProperty]
        private string _level0Subtitle = "12 Cantos • Complete Standard Edition";

        [ObservableProperty]
        private string _backToLevel0ButtonText = "← Back to Cantos";

        [ObservableProperty]
        private ObservableCollection<CantoNode> _cantos = new();

        [ObservableProperty]
        private CantoNode? _selectedCanto;

        [ObservableProperty]
        private ChapterNode? _selectedChapter;

        [ObservableProperty]
        private string _chapterListTitle = string.Empty;

        [ObservableProperty]
        private string _chapterListSubtitle = string.Empty;

        [ObservableProperty]
        private bool _isCantoListVisible;

        [ObservableProperty]
        private bool _isChapterListVisible = true;

        [ObservableProperty]
        private bool _isVerseListVisible = false;

        [ObservableProperty]
        private ObservableCollection<ChapterNode> _displayedChapters = new();

        public void LoadBook(BookNode? book)
        {
            if (book == null || book.IsFolder || book.IsHeader) return;
            CurrentBook = book;
            IsSb = book.BookKey == "SB";
            IsCc = book.BookKey == "CC";
            HasLevel0 = IsSb || IsCc;

            if (book.Chapters == null) return;

            if (IsSb)
            {
                Level0Subtitle = "12 Cantos • Complete Standard Edition";
                BackToLevel0ButtonText = "← Back to Cantos";

                var cantoGroups = new Dictionary<int, List<ChapterNode>>();
                for (int c = 1; c <= 12; c++) cantoGroups[c] = new List<ChapterNode>();

                foreach (var ch in book.Chapters)
                {
                    var match = Regex.Match(ch.Title, @"Canto\s+(\d+)", RegexOptions.IgnoreCase);
                    int cantoNum = match.Success ? int.Parse(match.Groups[1].Value) : 1;
                    if (!cantoGroups.ContainsKey(cantoNum)) cantoGroups[cantoNum] = new List<ChapterNode>();
                    cantoGroups[cantoNum].Add(ch);
                }

                var sbCantoList = new List<CantoNode>();
                foreach (var kvp in cantoGroups.OrderBy(x => x.Key))
                {
                    if (kvp.Value.Count == 0) continue;
                    string sub = CantoTitles.TryGetValue(kvp.Key, out var s) ? s : "";
                    sbCantoList.Add(new CantoNode
                    {
                        CantoNumber = kvp.Key,
                        Title = $"Canto {kvp.Key}: {sub}".TrimEnd(':', ' '),
                        Subtitle = sub,
                        Chapters = kvp.Value
                    });
                }
                Cantos = new ObservableCollection<CantoNode>(sbCantoList);

                SelectedCanto = null;
                SelectedChapter = null;
                ChapterListTitle = string.Empty;
                ChapterListSubtitle = string.Empty;
                IsCantoListVisible = true;
                IsChapterListVisible = false;
                IsVerseListVisible = false;
            }
            else if (IsCc)
            {
                Level0Subtitle = "3 Līlās • Complete Standard Edition";
                BackToLevel0ButtonText = "← Back to Līlās";

                var adiChapters = new List<ChapterNode>();
                var madhyaChapters = new List<ChapterNode>();
                var antyaChapters = new List<ChapterNode>();

                foreach (var ch in book.Chapters)
                {
                    if (ch.Title.Contains("Adi", StringComparison.OrdinalIgnoreCase) || ch.Title.Contains("Ādi", StringComparison.OrdinalIgnoreCase))
                    {
                        adiChapters.Add(ch);
                    }
                    else if (ch.Title.Contains("Madhya", StringComparison.OrdinalIgnoreCase))
                    {
                        madhyaChapters.Add(ch);
                    }
                    else if (ch.Title.Contains("Antya", StringComparison.OrdinalIgnoreCase))
                    {
                        antyaChapters.Add(ch);
                    }
                }

                Cantos = new ObservableCollection<CantoNode>(new List<CantoNode>
                {
                    new CantoNode
                    {
                        CantoNumber = 1,
                        Title = "Ādi-līlā: The Lord's Early Pastimes",
                        Subtitle = "The Early Pastimes (17 Chapters)",
                        Chapters = adiChapters
                    },
                    new CantoNode
                    {
                        CantoNumber = 2,
                        Title = "Madhya-līlā: The Lord's Middle Pastimes",
                        Subtitle = "The Middle Pastimes (25 Chapters)",
                        Chapters = madhyaChapters
                    },
                    new CantoNode
                    {
                        CantoNumber = 3,
                        Title = "Antya-līlā: The Lord's Final Pastimes",
                        Subtitle = "The Final Pastimes (20 Chapters)",
                        Chapters = antyaChapters
                    }
                });

                SelectedCanto = null;
                SelectedChapter = null;
                ChapterListTitle = string.Empty;
                ChapterListSubtitle = string.Empty;
                IsCantoListVisible = true;
                IsChapterListVisible = false;
                IsVerseListVisible = false;
            }
            else
            {
                DisplayedChapters = new ObservableCollection<ChapterNode>(book.Chapters);

                SelectedCanto = null;
                SelectedChapter = null;
                ChapterListTitle = book.Title;
                ChapterListSubtitle = $"{DisplayedChapters.Count} chapters";
                IsCantoListVisible = false;
                IsChapterListVisible = true;
                IsVerseListVisible = false;
            }
        }

        public void SelectCanto(CantoNode canto)
        {
            SelectedCanto = canto;
            var selectedCantoChapters = new List<ChapterNode>();
            foreach (var ch in canto.Chapters)
            {
                string title = ch.Title;
                if (IsCc)
                {
                    // Strip the redundant leading līlā prefix (e.g. "Ādi-līlā
                    // Chapter 1: ..." or "Ādi-līlā Dedication") since the
                    // canto view already makes clear which līlā is selected -
                    // keep the full chapter number + canonical title, or the
                    // bare front-matter section name.
                    var m = Regex.Match(ch.Title, @"(Chapter\s+\d+.*|Dedication|Preface|Introduction|Foreword)$", RegexOptions.IgnoreCase);
                    if (m.Success) title = m.Groups[1].Value;
                }
                else if (IsSb)
                {
                    // Strip the redundant "Canto N " prefix (the canto view
                    // already makes clear which canto is selected) - keep
                    // the full "Chapter N: Title".
                    var m = Regex.Match(ch.Title, @"(Chapter\s+\d+.*)$", RegexOptions.IgnoreCase);
                    if (m.Success) title = m.Groups[1].Value;
                }
                selectedCantoChapters.Add(new ChapterNode { Title = title, Records = ch.Records });
            }
            DisplayedChapters = new ObservableCollection<ChapterNode>(selectedCantoChapters);

            ChapterListTitle = canto.Title;
            ChapterListSubtitle = $"{DisplayedChapters.Count} chapters • {canto.TotalVerses} verses";
            IsCantoListVisible = false;
            IsChapterListVisible = true;
            IsVerseListVisible = false;
        }

        public void SelectChapter(ChapterNode chapter)
        {
            SelectedChapter = chapter;
            IsCantoListVisible = false;
            IsChapterListVisible = false;
            IsVerseListVisible = true;
        }

        public void BackToCantos()
        {
            SelectedCanto = null;
            SelectedChapter = null;
            ChapterListTitle = string.Empty;
            ChapterListSubtitle = string.Empty;
            IsCantoListVisible = true;
            IsChapterListVisible = false;
            IsVerseListVisible = false;
        }

        public void BackToChapters()
        {
            SelectedChapter = null;
            IsCantoListVisible = false;
            IsChapterListVisible = true;
            IsVerseListVisible = false;
        }

        public void ClearSelection()
        {
            if (HasLevel0)
            {
                BackToCantos();
            }
            else
            {
                SelectedChapter = null;
                IsCantoListVisible = false;
                IsChapterListVisible = true;
                IsVerseListVisible = false;
            }
        }
    }
}
