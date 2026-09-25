using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.UI.Views
{
    public class BookCheckItem : INotifyPropertyChanged
    {
        public string BookKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed partial class AdvancedSearchDialog : ContentDialog
    {
        private readonly ICorpusRepository _repository;
        public ObservableCollection<VocabTerm> VocabTerms { get; } = new();
        public ObservableCollection<BookCheckItem> BookItems { get; } = new();

        public string QueryResult => QueryForTextBox.Text?.Trim() ?? string.Empty;
        public string? SelectedScope => (ScopeComboBox.SelectedItem as ComboBoxItem)?.Content as string;

        public List<string> GetCheckedBookKeys()
        {
            if (BookItems.All(b => b.IsChecked))
            {
                // All checked -> no filter needed
                return new List<string>();
            }

            return BookItems.Where(b => b.IsChecked).Select(b => b.BookKey).ToList();
        }

        public AdvancedSearchDialog(ICorpusRepository repository, string initialQuery = "")
        {
            _repository = repository;
            this.InitializeComponent();

            QueryForTextBox.Text = initialQuery;
            Loaded += AdvancedSearchDialog_Loaded;
        }

        private async void AdvancedSearchDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Populate Books
            try
            {
                var hierarchy = await _repository.GetLibraryHierarchyAsync();
                BookItems.Clear();
                foreach (var b in hierarchy)
                {
                    BookItems.Add(new BookCheckItem
                    {
                        BookKey = b.BookKey,
                        Title = b.Title,
                        IsChecked = true
                    });
                }
                BookChecklistRepeater.ItemsSource = BookItems;
                UpdateCheckedCountDisplay();
            }
            catch { }

            // 2. Initial Word Wheel Population
            await LoadVocabPrefixAsync(string.Empty);

            if (this.XamlRoot != null)
            {
                this.XamlRoot.Changed += (s, args) =>
                {
                    if (_isMaximized)
                    {
                        ApplyMaximizeState();
                    }
                };
            }
        }

        private bool _isMaximized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            _isMaximized = !_isMaximized;
            ApplyMaximizeState();
        }

        private void ApplyMaximizeState()
        {
            if (this.XamlRoot == null) return;

            double windowWidth = this.XamlRoot.Size.Width;
            double windowHeight = this.XamlRoot.Size.Height;

            if (_isMaximized)
            {
                // True full screen: adapt to window size
                this.Resources["ContentDialogMaxWidth"] = windowWidth;
                this.Resources["ContentDialogMaxHeight"] = windowHeight;
                this.Resources["ContentDialogMinWidth"] = windowWidth;
                this.Resources["ContentDialogMinHeight"] = windowHeight;

                this.MinWidth = windowWidth;
                this.MaxWidth = windowWidth;
                this.Width = windowWidth;

                this.MinHeight = windowHeight;
                this.MaxHeight = windowHeight;
                this.Height = windowHeight;

                this.Padding = new Thickness(16);

                DialogRootGrid.Width = double.NaN;
                DialogRootGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                DialogScrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
                DialogScrollViewer.VerticalAlignment = VerticalAlignment.Stretch;
                DialogScrollViewer.MaxHeight = Math.Max(300, windowHeight - 140);
                DialogScrollViewer.Height = Math.Max(300, windowHeight - 140);

                MaximizeIcon.Glyph = "\uE923"; // Restore
                ToolTipService.SetToolTip(MaximizeButton, "Restore Mini Dialog");
            }
            else
            {
                // Mini compact modal
                const double miniWidth = 760;
                const double miniHeight = 680;

                this.Resources["ContentDialogMaxWidth"] = miniWidth;
                this.Resources["ContentDialogMaxHeight"] = miniHeight;
                this.Resources["ContentDialogMinWidth"] = 720.0;
                this.Resources["ContentDialogMinHeight"] = 400.0;

                this.MinWidth = 720;
                this.MaxWidth = miniWidth;
                this.Width = miniWidth;

                this.MinHeight = 400;
                this.MaxHeight = miniHeight;
                this.Height = double.NaN;

                this.Padding = new Thickness(24);

                DialogRootGrid.Width = 720;
                DialogRootGrid.HorizontalAlignment = HorizontalAlignment.Center;
                DialogScrollViewer.MaxHeight = 540;
                DialogScrollViewer.Height = double.NaN;
                DialogScrollViewer.VerticalAlignment = VerticalAlignment.Top;

                MaximizeIcon.Glyph = "\uE922"; // Maximize
                ToolTipService.SetToolTip(MaximizeButton, "Maximize to Full Screen");
            }

            // Safely size the ContentDialog's own template elements without ever touching external parents
            try
            {
                SetVisualTreeSizing(this, windowWidth, windowHeight, _isMaximized);
            }
            catch { }
        }

        private void SetVisualTreeSizing(DependencyObject parent, double w, double h, bool isMaximized)
        {
            if (parent == null) return;
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe)
                {
                    if (fe.Name == "BackgroundElement" || fe.Name == "LayoutRoot" || fe.Name == "Container" || fe.Name == "ContentRoot")
                    {
                        if (isMaximized)
                        {
                            fe.MinWidth = w;
                            fe.MaxWidth = w;
                            fe.Width = w;
                            fe.MinHeight = h;
                            fe.MaxHeight = h;
                            fe.Height = h;
                            fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                            fe.VerticalAlignment = VerticalAlignment.Stretch;
                            fe.Margin = new Thickness(0);
                        }
                        else
                        {
                            fe.MinWidth = 720;
                            fe.MaxWidth = 760;
                            fe.Width = 760;
                            fe.MinHeight = 400;
                            fe.MaxHeight = 680;
                            fe.Height = double.NaN;
                            fe.HorizontalAlignment = HorizontalAlignment.Center;
                            fe.VerticalAlignment = VerticalAlignment.Center;
                            fe.Margin = new Thickness(0);
                        }
                    }
                }
                SetVisualTreeSizing(child, w, h, isMaximized);
            }
        }



        private async Task LoadVocabPrefixAsync(string prefix)
        {
            try
            {
                var terms = await _repository.GetVocabularyTermsAsync(prefix, limit: 60);
                VocabTerms.Clear();
                foreach (var t in terms)
                {
                    VocabTerms.Add(t);
                }

                if (VocabTerms.Count > 0)
                {
                    int targetIndex = 0;
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        string cleanPrefix = prefix.Trim().ToLowerInvariant();
                        for (int i = 0; i < VocabTerms.Count; i++)
                        {
                            if (VocabTerms[i].Term.StartsWith(cleanPrefix, StringComparison.OrdinalIgnoreCase) ||
                                string.Compare(VocabTerms[i].Term, cleanPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                targetIndex = i;
                                break;
                            }
                        }
                    }

                    WordWheelListView.SelectedIndex = targetIndex;
                    WordWheelListView.ScrollIntoView(VocabTerms[targetIndex]);
                }
            }
            catch { }
        }

        private async void WordInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string prefix = WordInputBox.Text?.Trim() ?? string.Empty;
            await LoadVocabPrefixAsync(prefix);
        }

        private void WordInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                InsertCurrentWord();
                e.Handled = true;
            }
        }

        private void WordWheelListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WordWheelListView.SelectedItem is VocabTerm term)
            {
                SelectedWordHitsText.Text = $"{term.Term}  —  {term.DocumentCount:N0} records with hits";
                SelectedWordOccurrencesText.Text = $"{term.TotalOccurrences:N0} total occurrences across the corpus";
            }
        }

        private void WordWheelListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            InsertCurrentWord();
        }

        private void InsertWordButton_Click(object sender, RoutedEventArgs e)
        {
            InsertCurrentWord();
        }

        private void InsertCurrentWord()
        {
            string wordToInsert = string.Empty;
            if (WordWheelListView.SelectedItem is VocabTerm term)
            {
                wordToInsert = term.Term;
            }
            else if (!string.IsNullOrWhiteSpace(WordInputBox.Text))
            {
                wordToInsert = WordInputBox.Text.Trim();
            }

            if (!string.IsNullOrEmpty(wordToInsert))
            {
                AppendToQuery(wordToInsert);
            }
        }

        private void OperatorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string op)
            {
                AppendToQuery(op);
            }
        }

        private void PhraseButton_Click(object sender, RoutedEventArgs e)
        {
            var text = QueryForTextBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text) && !text.StartsWith("\""))
            {
                QueryForTextBox.Text = $"\"{text}\"";
            }
            else
            {
                AppendToQuery("\"\"");
            }
        }

        private void AppendToQuery(string token)
        {
            string current = QueryForTextBox.Text ?? "";
            if (token.StartsWith(" ") || current.EndsWith(" ") || string.IsNullOrEmpty(current))
            {
                QueryForTextBox.Text = current + token;
            }
            else
            {
                QueryForTextBox.Text = current + " " + token;
            }
            QueryForTextBox.SelectionStart = QueryForTextBox.Text.Length;
            QueryForTextBox.Focus(FocusState.Programmatic);
        }

        private void BookCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCheckedCountDisplay();
        }

        private void UpdateCheckedCountDisplay()
        {
            int checkedCount = BookItems.Count(b => b.IsChecked);
            int totalCount = BookItems.Count;
            if (checkedCount == totalCount)
            {
                CheckedCountText.Text = $"All {totalCount} Books Checked";
            }
            else if (checkedCount == 0)
            {
                CheckedCountText.Text = "No Books Checked (0)";
            }
            else
            {
                CheckedCountText.Text = $"{checkedCount} of {totalCount} Books Checked";
            }
        }

        private void SelectAllBooks_Click(object sender, RoutedEventArgs e)
        {
            foreach (var b in BookItems) b.IsChecked = true;
            UpdateCheckedCountDisplay();
        }

        private void ClearAllBooks_Click(object sender, RoutedEventArgs e)
        {
            foreach (var b in BookItems) b.IsChecked = false;
            UpdateCheckedCountDisplay();
        }

        private void SelectScripturesOnly_Click(object sender, RoutedEventArgs e)
        {
            var scriptureKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "BG", "SB", "CC", "DI", "MADHYA", "ANTYA", "NOD", "TLC", "NOI", "ISO", "BS", "BQ"
            };

            foreach (var b in BookItems)
            {
                b.IsChecked = scriptureKeys.Contains(b.BookKey);
            }
            UpdateCheckedCountDisplay();
        }

        private void SelectBiographiesOnly_Click(object sender, RoutedEventArgs e)
        {
            var bioKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SPL", "SSR", "TKG"
            };

            foreach (var b in BookItems)
            {
                b.IsChecked = bioKeys.Contains(b.BookKey);
            }
            UpdateCheckedCountDisplay();
        }

        private void ExampleQuery_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton btn && btn.Tag is string q)
            {
                QueryForTextBox.Text = q;
                QueryForTextBox.SelectionStart = QueryForTextBox.Text.Length;
            }
        }
    }
}
