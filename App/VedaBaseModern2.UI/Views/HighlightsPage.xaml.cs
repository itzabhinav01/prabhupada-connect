using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class HighlightsPage : Page
    {
        public HighlightsViewModel ViewModel { get; }

        public HighlightsPage()
        {
            this.InitializeComponent();
            ViewModel = new HighlightsViewModel(App.Current.Repository, App.Current.UserRepository);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadHighlightsAsync();

            string? autoScripture = Environment.GetEnvironmentVariable("VEDABASE_TEST_FILTER_SCRIPTURE");
            if (!string.IsNullOrEmpty(autoScripture))
            {
                var opt = ViewModel.ScriptureFilterOptions.FirstOrDefault(o => o.BookKey == autoScripture || o.Name.Contains(autoScripture, StringComparison.OrdinalIgnoreCase));
                if (opt != null) ViewModel.SelectedScriptureFilter = opt;
            }

            string? autoColor = Environment.GetEnvironmentVariable("VEDABASE_TEST_FILTER_COLOR");
            if (!string.IsNullOrEmpty(autoColor))
            {
                var opt = ViewModel.ColorFilterOptions.FirstOrDefault(o => o.Name.Equals(autoColor, StringComparison.OrdinalIgnoreCase));
                if (opt != null) ViewModel.SelectedColorFilter = opt;
            }

            string? autoSearch = Environment.GetEnvironmentVariable("VEDABASE_TEST_SEARCH_HIGHLIGHTS");
            if (!string.IsNullOrEmpty(autoSearch))
            {
                ViewModel.SearchText = autoSearch;
            }
        }

        private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is HighlightListItem item)
            {
                // Phase 4.9.4: a precision (non-legacy) highlight carries its
                // exact Field + character range - navigate straight to that
                // passage instead of just the chapter start. A legacy
                // (Phase 4.6) block-level highlight has no such range (-1
                // sentinel), so it falls back to plain RecordKey navigation
                // exactly as before.
                if (!item.IsLegacyBlockLevel && item.StartOffset >= 0 && item.Length > 0)
                {
                    this.Frame.Navigate(typeof(ReadingPage), new HighlightNavigationTarget(item.RecordKey, item.Field, item.StartOffset, item.Length));
                }
                else
                {
                    this.Frame.Navigate(typeof(ReadingPage), item.RecordKey);
                }
            }
            if (sender is ListView lv) lv.SelectedItem = null;
        }

        private async void RemoveHighlight_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string highlightId)
            {
                await ViewModel.RemoveHighlightAsync(highlightId);
            }
        }

        public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowEmptyState(bool isLoading, int count) => (!isLoading && count == 0) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CountToVis(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // The "no highlights match this filter" compact empty state only
        // applies once there IS at least one highlight overall (otherwise
        // the plain "No highlights yet." empty state above already covers it).
        public Visibility ShowFilterEmptyState(bool isFilterEmpty, int totalCount) => (isFilterEmpty && totalCount > 0) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowFilteredResults(bool isFilterEmpty, int totalCount) => (!isFilterEmpty && totalCount > 0) ? Visibility.Visible : Visibility.Collapsed;
    }
}
