using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VedaBaseModern.Core.Models;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class LibraryPage : Page
    {
        public LibraryViewModel ViewModel { get; }

        public LibraryPage()
        {
            this.InitializeComponent();
            ViewModel = new LibraryViewModel();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is BookNode book)
            {
                ViewModel.LoadBook(book);
            }
        }

        private void Canto_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is CantoNode canto)
            {
                ViewModel.SelectCanto(canto);
            }
            if (sender is ListView lv)
            {
                lv.SelectedItem = null;
            }
        }

        private void BackToCantos_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.BackToCantos();
        }

        private void Chapter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ChapterNode chapter)
            {
                // Prose-book chapters carry exactly one record (the whole
                // chapter/section IS the record) - skip the redundant
                // single-item verse list and go straight to Reading.
                if (chapter.Records.Count == 1)
                {
                    this.Frame.Navigate(typeof(ReadingPage), chapter.Records[0].RecordKey);
                }
                else
                {
                    ViewModel.SelectChapter(chapter);
                }
            }
            if (sender is ListView lv)
            {
                lv.SelectedItem = null;
            }
        }

        private void BackToChapters_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.BackToChapters();
        }

        private void Record_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is RecordNode record)
            {
                // Navigate to ReadingPage
                this.Frame.Navigate(typeof(ReadingPage), record.RecordKey);
            }
            
            if (sender is ListView lv)
            {
                lv.SelectedItem = null;
            }
        }

        public string ChapterListHeading(bool isSb, string? bookTitle, string? cantoTitle)
        {
            if (isSb && !string.IsNullOrWhiteSpace(cantoTitle)) return cantoTitle;
            return bookTitle ?? "Chapters";
        }

        public string ChapterCountText(int count) => $"{count} {(count == 1 ? "chapter" : "chapters")}";

        public Visibility StringToVis(string? s) => string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility VerseCountVis(int count) => count > 1 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        public Visibility InverseBoolToVis(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    }
}
