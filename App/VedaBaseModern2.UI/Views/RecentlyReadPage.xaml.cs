using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern.Core.Models;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class RecentlyReadPage : Page
    {
        public RecentlyReadViewModel ViewModel { get; }

        public RecentlyReadPage()
        {
            this.InitializeComponent();
            ViewModel = new RecentlyReadViewModel(App.Current.Repository, App.Current.UserRepository);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadAsync();
        }

        private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is RecentlyReadItem item)
            {
                if (item.IsAvailable)
                {
                    this.Frame.Navigate(typeof(ReadingPage), item.RecordKey);
                }
            }

            if (sender is ListView lv)
            {
                lv.SelectedItem = null;
            }
        }

        private void ContinueReadingButton_Click(object sender, RoutedEventArgs e)
        {
            var item = ViewModel.ContinueReadingItem;
            if (item != null && item.IsAvailable)
            {
                this.Frame.Navigate(typeof(ReadingPage), item.RecordKey);
            }
        }

        private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ClearHistoryCommand.ExecuteAsync(null);
            ClearHistoryFlyoutButton.Flyout?.Hide();
        }

        public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        public Visibility InverseBoolToVis(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ItemToVis(RecentlyReadItem? item) => item != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowEmptyState(bool isLoading, int count) => (!isLoading && count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }
}
