using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Services;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public class SearchNavigationArgs
    {
        public string Query { get; set; } = string.Empty;
        public List<string>? CheckedBookKeys { get; set; }
        public string? FieldScope { get; set; }
        public bool AutoExecute { get; set; } = true;
    }

    public sealed partial class SearchPage : Page
    {
        public SearchViewModel ViewModel { get; }

        public SearchPage()
        {
            this.InitializeComponent();
            ViewModel = new SearchViewModel(App.Current.SearchService, App.Current.Repository, App.Current.ReferenceService);
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string query)
            {
                ViewModel.SearchText = query;
                if (!ViewModel.IsReferenceMode)
                {
                    await ViewModel.ExecuteSearchCommand.ExecuteAsync(null);
                }
            }
            else if (e.Parameter is SearchNavigationArgs navArgs)
            {
                ViewModel.SearchText = navArgs.Query;
                ViewModel.CheckedBookKeys = navArgs.CheckedBookKeys;
                if (!string.IsNullOrEmpty(navArgs.FieldScope))
                {
                    ViewModel.SelectedFieldScope = navArgs.FieldScope;
                }
                if (navArgs.AutoExecute && !ViewModel.IsReferenceMode)
                {
                    await ViewModel.ExecuteSearchCommand.ExecuteAsync(null);
                }
            }

            string? autoFacet = Environment.GetEnvironmentVariable("VEDABASE_TEST_SEARCH_FACET");
            if (int.TryParse(autoFacet, out int facetIdx))
            {
                ViewModel.SelectedFacetIndex = facetIdx;
            }
        }

        private async void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            // "@..." + Enter -> direct navigation, no results page at all.
            if (ViewModel.IsReferenceMode)
            {
                // A suggestion may already have been chosen (args.ChosenSuggestion);
                // if so, navigate immediately using its RecordKey rather than
                // re-resolving the raw text.
                if (args.ChosenSuggestion is ReferenceSuggestion chosen && chosen.RecordKey != null)
                {
                    this.Frame.Navigate(typeof(ReadingPage), chosen.RecordKey);
                    return;
                }

                var recordKey = await ViewModel.TryResolveDirectReferenceAsync();
                if (recordKey != null)
                {
                    this.Frame.Navigate(typeof(ReadingPage), recordKey);
                }
                else
                {
                    // Doesn't (yet) name a complete, existing verse - stay on
                    // the search box rather than running a full-text search
                    // against literal "@..." text (which would just find 0
                    // results and confuse the direct-reference intent).
                    ViewModel.StatusText = $"'{ViewModel.SearchText}' doesn't match a known verse reference yet.";
                }
                return;
            }

            await ViewModel.ExecuteSearchCommand.ExecuteAsync(null);
        }

        private void AutoSuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not ReferenceSuggestion suggestion) return;

            if (suggestion.RecordKey != null)
            {
                // Leaf verse clicked - navigate immediately, no Enter needed.
                this.Frame.Navigate(typeof(ReadingPage), suggestion.RecordKey);
            }
            else
            {
                // Intermediate level (a work or a chapter/canto) clicked - fill
                // the box with the narrower query and let it keep suggesting,
                // matching the "progressively narrows" autocomplete requirement.
                ViewModel.SearchText = suggestion.QueryToComplete;
            }
        }

        private void BookFilterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                ViewModel.UpdateFilteredBookItems(tb.Text);
            }
        }

        private void SelectAllBooks_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectAllBooks(true);
            RefreshBookFilterListView();
        }

        private void ClearAllBooks_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectAllBooks(false);
            RefreshBookFilterListView();
        }

        private void RefreshBookFilterListView()
        {
            var items = ViewModel.FilteredBookItems;
            BookFilterListView.ItemsSource = null;
            BookFilterListView.ItemsSource = items;
        }

        private void BookCheckBox_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.UpdateSelectedBooksFromCheckboxes();
        }

        private async void ApplyBookFilter_Click(object sender, RoutedEventArgs e)
        {
            BookFilterFlyout.Hide();
            ViewModel.UpdateSelectedBooksFromCheckboxes();
            if (!string.IsNullOrWhiteSpace(ViewModel.SearchText))
            {
                await ViewModel.ExecuteSearchCommand.ExecuteAsync(null);
            }
        }

        private async void AdvancedSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            await OpenAdvancedSearchDialogAsync();
        }

        private async void AdvancedSearchAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            await OpenAdvancedSearchDialogAsync();
        }

        private async System.Threading.Tasks.Task OpenAdvancedSearchDialogAsync()
        {
            try
            {
                var dialog = new AdvancedSearchDialog(App.Current.Repository, ViewModel.SearchText);
                VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

                var res = await dialog.ShowAsync();
                if (res == ContentDialogResult.Primary || res == ContentDialogResult.Secondary)
                {
                    if (!string.IsNullOrWhiteSpace(dialog.QueryResult))
                    {
                        ViewModel.SearchText = dialog.QueryResult;
                    }

                    if (!string.IsNullOrWhiteSpace(dialog.SelectedScope))
                    {
                        ViewModel.SelectedFieldScope = dialog.SelectedScope;
                    }

                    ViewModel.CheckedBookKeys = dialog.GetCheckedBookKeys();

                    if (res == ContentDialogResult.Primary)
                    {
                        await ViewModel.ExecuteSearchCommand.ExecuteAsync(null);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SearchPage] AdvancedSearchDialog error: {ex}");
            }
        }

        private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SearchResult result)
            {
                // Phase 4.9.4: a highlight match carries the exact Field +
                // character range it was found in - navigate straight to that
                // passage instead of just the chapter start.
                if (result.Category == "Highlight" && !string.IsNullOrEmpty(result.Field) && result.StartOffset >= 0 && result.Length > 0)
                {
                    this.Frame.Navigate(typeof(ReadingPage), new HighlightNavigationTarget(result.RecordKey, result.Field, result.StartOffset, result.Length));
                }
                else
                {
                    // Pass search query so the reader activates the in-document hit navigation HUD
                    this.Frame.Navigate(typeof(ReadingPage), new SearchResultNavigationTarget(result.RecordKey, ViewModel.SearchText));
                }
            }

            if (sender is ListView lv)
            {
                lv.SelectedItem = null;
            }
        }

        private void EscapeAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (string.IsNullOrEmpty(ViewModel.SearchText) && ViewModel.Results.Count == 0) return;
            ViewModel.SearchText = string.Empty;
            ViewModel.Results.Clear();
            ViewModel.StatusText = "Search the VedaBase";
            MainSearchBox.IsSuggestionListOpen = false;
            args.Handled = true;
        }

        public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        public Visibility InverseBoolToVis(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
        public bool Not(bool b) => !b;
    }
}
