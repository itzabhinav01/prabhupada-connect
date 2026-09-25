using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VedaBaseModern.Core.Models;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class BookmarksPage : Page
    {
        public BookmarksViewModel ViewModel { get; }

        public BookmarksPage()
        {
            this.InitializeComponent();
            ViewModel = new BookmarksViewModel(App.Current.Repository, App.Current.UserRepository);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadBookmarksAsync();
        }

        private async void NewCollectionButton_Click(object sender, RoutedEventArgs e)
        {
            var box = new TextBox { PlaceholderText = "e.g. Daily Reading" };
            var errorText = new TextBlock
            {
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD3, 0x2F, 0x2F)),
                FontSize = 12,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var panel = new StackPanel { Spacing = 8, Width = 280 };
            panel.Children.Add(new TextBlock { Text = "Collection name:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(box);
            panel.Children.Add(errorText);

            var dialog = new ContentDialog
            {
                Title = "Create Bookmark Collection",
                Content = panel,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                XamlRoot = this.XamlRoot
            };
            VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

            box.TextChanged += (s, args) =>
            {
                string trimmed = box.Text.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    dialog.IsPrimaryButtonEnabled = false;
                    errorText.Visibility = Visibility.Collapsed;
                }
                else if (ViewModel.Collections.Any(c => c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    dialog.IsPrimaryButtonEnabled = false;
                    errorText.Text = "A collection with this name already exists.";
                    errorText.Visibility = Visibility.Visible;
                }
                else
                {
                    dialog.IsPrimaryButtonEnabled = true;
                    errorText.Visibility = Visibility.Collapsed;
                }
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
            {
                await ViewModel.CreateCollectionCommand.ExecuteAsync(box.Text.Trim());
            }
        }

        // Phase 4.9.1: rename/delete now act on whatever collection is
        // currently selected in the filter picker, rather than a per-section
        // button next to a giant collection heading - same underlying
        // ViewModel calls (and same confirmation copy) as before.
        private async void RenameSelectedCollection_Click(object sender, RoutedEventArgs e)
        {
            var filter = ViewModel.SelectedFilter;
            if (filter == null || !filter.IsRealCollection || filter.CollectionId == null) return;

            var box = new TextBox { Text = filter.Name, PlaceholderText = "Collection name" };
            var errorText = new TextBlock
            {
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD3, 0x2F, 0x2F)),
                FontSize = 12,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var panel = new StackPanel { Spacing = 8, Width = 280 };
            panel.Children.Add(new TextBlock { Text = "Collection name:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(box);
            panel.Children.Add(errorText);

            var dialog = new ContentDialog
            {
                Title = "Rename collection",
                Content = panel,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = true,
                XamlRoot = this.XamlRoot
            };
            VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

            box.TextChanged += (s, args) =>
            {
                string trimmed = box.Text.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    dialog.IsPrimaryButtonEnabled = false;
                    errorText.Text = "Collection name cannot be empty.";
                    errorText.Visibility = Visibility.Visible;
                }
                else if (ViewModel.Collections.Any(c => c.Id != filter.CollectionId && c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    dialog.IsPrimaryButtonEnabled = false;
                    errorText.Text = "A collection with this name already exists.";
                    errorText.Visibility = Visibility.Visible;
                }
                else
                {
                    dialog.IsPrimaryButtonEnabled = true;
                    errorText.Visibility = Visibility.Collapsed;
                }
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
            {
                await ViewModel.RenameCollectionAsync(filter.CollectionId, box.Text.Trim());
            }
        }

        private async void DeleteSelectedCollection_Click(object sender, RoutedEventArgs e)
        {
            var filter = ViewModel.SelectedFilter;
            if (filter == null || !filter.IsRealCollection || filter.CollectionId == null) return;

            var dialog = new ContentDialog
            {
                Title = $"Delete \"{filter.Name}\"?",
                Content = "Bookmarks in this collection will not be deleted. They will be moved to Uncategorized.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteCollectionAsync(filter.CollectionId);
            }
        }

        private void OpenBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string recordKey)
            {
                this.Frame.Navigate(typeof(ReadingPage), recordKey);
            }
        }

        private async void RemoveBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string recordKey)
            {
                await ViewModel.RemoveBookmarkAsync(recordKey);
            }
        }

        private async void RemoveFromCollection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is BookmarkItem bi)
            {
                await ViewModel.MoveBookmarkAsync(bi.RecordKey, null);
            }
        }

        // Populates "Move to..." with every collection (minus the bookmark's
        // current one) plus "Uncategorized" - built fresh each open so a
        // collection created moments ago via the New Collection flyout is
        // always up to date, without a separate refresh hook.
        private void BookmarkActionsFlyout_Opening(object sender, object e)
        {
            if (sender is not MenuFlyout flyout) return;
            if (flyout.Items.Count == 0 || flyout.Items[0] is not MenuFlyoutSubItem moveTo) return;
            if (moveTo.Tag is not BookmarkItem item) return;

            if (flyout.Items.Count > 1 && flyout.Items[1] is MenuFlyoutItem removeFromColl)
            {
                removeFromColl.Tag = item;
                removeFromColl.Visibility = item.CollectionId != null ? Visibility.Visible : Visibility.Collapsed;
            }

            moveTo.Items.Clear();

            if (item.CollectionId != null)
            {
                var uncategorized = new MenuFlyoutItem { Text = "Uncategorized", Tag = item.RecordKey };
                uncategorized.Click += async (s, args) => await ViewModel.MoveBookmarkAsync(item.RecordKey, null);
                moveTo.Items.Add(uncategorized);
            }

            foreach (var c in ViewModel.Collections.Where(c => c.Id != item.CollectionId))
            {
                var mfi = new MenuFlyoutItem { Text = c.Name, Tag = item.RecordKey };
                mfi.Click += async (s, args) => await ViewModel.MoveBookmarkAsync(item.RecordKey, c.Id);
                moveTo.Items.Add(mfi);
            }

            if (moveTo.Items.Count == 0)
            {
                moveTo.Items.Add(new MenuFlyoutItem { Text = "No other collections yet", IsEnabled = false });
            }
        }

        public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        public Visibility InverseBoolToVis(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ShowEmptyState(bool isLoading, bool hasAny) => (!isLoading && !hasAny) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsRealCollectionVis(CollectionFilterOption? filter) => (filter?.IsRealCollection == true) ? Visibility.Visible : Visibility.Collapsed;
    }
}
