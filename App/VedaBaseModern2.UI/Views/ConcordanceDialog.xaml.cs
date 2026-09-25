using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VedaBaseModern.Core.Models;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class ConcordanceDialog : ContentDialog
    {
        public event Action<string>? VerseSelected;

        public ConcordanceDialog()
        {
            this.InitializeComponent();
        }

        private ConcordanceResult? _currentResult;
        private string? _selectedBookFilter;

        public async Task SearchAsync(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return;
            string cleanWord = word.Trim();

            WordInputBox.Text = cleanWord;
            WordTermText.Text = cleanWord;
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;
            MatchesListView.ItemsSource = null;
            BookChipsPanel.Children.Clear();
            _selectedBookFilter = null;

            try
            {
                var result = await App.Current.ConcordanceService.LookupWordAsync(cleanWord);
                _currentResult = result;

                // Primary Root Gloss: Consensus / Frequency-based modal gloss across all occurrences
                var glossGroups = result.AllMatches
                    .Where(m => !string.IsNullOrWhiteSpace(m.Gloss))
                    .GroupBy(m => m.Gloss.Trim().TrimEnd('.', ';', ','), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                if (glossGroups.Count > 0)
                {
                    var topGloss = glossGroups[0];
                    int topCount = topGloss.Count();
                    string glossText = $"Primary Gloss: \"{topGloss.Key}\" ({topCount} of {result.TotalCount} verses)";
                    if (glossGroups.Count > 1)
                    {
                        var otherCommon = glossGroups.Skip(1).Take(2).Select(g => $"\"{g.Key}\" ({g.Count()})");
                        glossText += $" • Also translated as: {string.Join(", ", otherCommon)}";
                    }
                    LexiconGlossText.Text = glossText;
                }
                else
                {
                    LexiconGlossText.Text = $"Attested across {result.BookGroups.Count} canonical scriptures in {result.TotalCount} verses.";
                }

                CountBadgeText.Text = $"{result.TotalCount} occurrences";
                MatchesListView.ItemsSource = result.AllMatches;

                // Generate book frequency chips
                if (result.BookGroups.Count > 0)
                {
                    var allBtn = new Button
                    {
                        Content = $"All Books ({result.TotalCount})",
                        Style = (Style)Application.Current.Resources["DefaultButtonStyle"],
                        Height = 32,
                        Padding = new Thickness(10, 4, 10, 4)
                    };
                    allBtn.Click += (s, e) =>
                    {
                        _selectedBookFilter = null;
                        ApplyCurrentFilter();
                    };
                    BookChipsPanel.Children.Add(allBtn);

                    foreach (var bg in result.BookGroups)
                    {
                        var bookBtn = new Button
                        {
                            Content = $"{bg.BookTitle} ({bg.Count})",
                            Style = (Style)Application.Current.Resources["DefaultButtonStyle"],
                            Height = 32,
                            Padding = new Thickness(10, 4, 10, 4)
                        };
                        string targetBk = bg.BookKey;
                        bookBtn.Click += (s, e) =>
                        {
                            _selectedBookFilter = targetBk;
                            ApplyCurrentFilter();
                        };
                        BookChipsPanel.Children.Add(bookBtn);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConcordanceDialog] Search error: {ex}");
                CountBadgeText.Text = "(Error performing lookup)";
                LexiconGlossText.Text = "Failed to retrieve linguistic concordance data.";
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async void WordInputBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                await SearchAsync(args.QueryText);
            }
        }

        private async void LookupButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(WordInputBox.Text))
            {
                await SearchAsync(WordInputBox.Text);
            }
        }

        private void MatchesListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ConcordanceMatch match && !string.IsNullOrEmpty(match.RecordKey))
            {
                this.Hide();
                VerseSelected?.Invoke(match.RecordKey);
            }
        }

        private void QuickFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyCurrentFilter();
        }

        private void ApplyCurrentFilter()
        {
            if (_currentResult == null) return;
            var list = _selectedBookFilter == null
                ? _currentResult.AllMatches
                : _currentResult.AllMatches.Where(m => m.BookKey == _selectedBookFilter);

            string filter = QuickFilterBox?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(filter))
            {
                list = list.Where(m =>
                    (m.Reference != null && m.Reference.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                    (m.Snippet != null && m.Snippet.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                    (m.Gloss != null && m.Gloss.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                    (m.BookTitle != null && m.BookTitle.Contains(filter, StringComparison.OrdinalIgnoreCase)));
            }
            MatchesListView.ItemsSource = list.ToList();
        }
    }
}
