using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VedaBaseModern.UI.Views;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern.UI.Services;
using VedaBaseModern.UI.Messages;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using CommunityToolkit.Mvvm.Messaging;

namespace VedaBaseModern_UI;

public sealed partial class MainPage : Page
{
    public static MainPage? Current { get; private set; }
    public MainViewModel ViewModel { get; }

    // Segoe Fluent glyphs for the library-collapse chevron: points left when
    // the pane is open (click to close, "push it that way"), right when
    // closed (click to open). Purely cosmetic direction cue - the click
    // handler is the same either way.
    private const string ChevronLeftGlyph = "";
    private const string ChevronRightGlyph = "";

    public MainPage()
    {
        Current = this;
        InitializeComponent();
        this.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        ViewModel = new MainViewModel(App.Current.Repository, App.Current.UserRepository);
        Loaded += MainPage_Loaded;

        WeakReferenceMessenger.Default.Register<FocusModeChangedMessage>(this, (recipient, message) =>
        {
            NavView.IsPaneVisible = !message.Value;
        });

        this.ActualThemeChanged += (s, e) => UpdateThemeToggleVisual();
        App.Current.ThemeChanged += (theme) => UpdateThemeToggleVisual();
    }

    private void UpdateThemeToggleVisual()
    {
        bool isDark = this.ActualTheme == ElementTheme.Dark;
        MainThemeToggleIcon.Glyph = isDark ? "\uE708" : "\uE706";
    }

    private async void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        await App.Current.ToggleThemeAsync();
        UpdateThemeToggleVisual();
    }

    private async void ThemeAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        await App.Current.ToggleThemeAsync();
        UpdateThemeToggleVisual();
        args.Handled = true;
    }

    public Frame CurrentFrame
    {
        get
        {
            if (ContentTabs.SelectedItem is TabViewItem tab && tab.Content is Frame f)
                return f;
            if (ContentTabs.TabItems.Count > 0 && ContentTabs.TabItems[0] is TabViewItem firstTab && firstTab.Content is Frame firstF)
                return firstF;
            return CreateNewTab("Reading", "\uE8A5", typeof(ReadingPage), "BG-1-1");
        }
    }

    public Frame CreateNewTab(string title, string glyph, Type pageType, object? parameter = null)
    {
        var frame = new Frame();
        var tab = new TabViewItem
        {
            Header = title,
            IconSource = new FontIconSource { Glyph = glyph, FontSize = 14 },
            Content = frame
        };

        ContentTabs.TabItems.Add(tab);
        ContentTabs.SelectedItem = tab;

        if (parameter != null)
            frame.Navigate(pageType, parameter);
        else
            frame.Navigate(pageType);

        return frame;
    }

    public void NavigateCurrentTab(Type pageType, object? parameter = null, string? tabTitle = null, string? glyph = null)
    {
        var frame = CurrentFrame;
        if (parameter != null)
            frame.Navigate(pageType, parameter);
        else
            frame.Navigate(pageType);

        if (ContentTabs.SelectedItem is TabViewItem tab)
        {
            if (!string.IsNullOrEmpty(tabTitle)) tab.Header = tabTitle;
            if (!string.IsNullOrEmpty(glyph)) tab.IconSource = new FontIconSource { Glyph = glyph, FontSize = 14 };
        }
    }

    public void UpdateTabHeaderForContent(UIElement content, string title, string? glyph = null)
    {
        foreach (var item in ContentTabs.TabItems)
        {
            if (item is TabViewItem tab && ((object?)tab.Content == (object?)content || (tab.Content is Frame f && (object?)f.Content == (object?)content)))
            {
                if (!string.IsNullOrEmpty(title)) tab.Header = title;
                if (!string.IsNullOrEmpty(glyph)) tab.IconSource = new FontIconSource { Glyph = glyph, FontSize = 14 };
                break;
            }
        }
    }

    private void ContentTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (sender.TabItems.Count > 1)
        {
            sender.TabItems.Remove(args.Tab);
        }
    }

    private void ContentTabs_AddTabButtonClick(TabView sender, object args)
    {
        var defaultBook = (NavView.SelectedItem as BookNode)
                          ?? ViewModel.Books.FirstOrDefault(b => !b.IsFolder && !b.IsHeader)
                          ?? ViewModel.Books.FirstOrDefault()?.Children?.FirstOrDefault();
        if (defaultBook != null)
        {
            CreateNewTab(defaultBook.Title, "\uE8A5", typeof(LibraryPage), defaultBook);
        }
        else
        {
            CreateNewTab("Scripture Library", "\uE8A5", typeof(LibraryPage));
        }
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadBooksAsync();

        string? startupVerse = Environment.GetEnvironmentVariable("VEDABASE_STARTUP_VERSE");
        if (!string.IsNullOrEmpty(startupVerse))
        {
            NavView.SelectedItem = null;
            CreateNewTab(startupVerse, "\uE8A5", typeof(ReadingPage), startupVerse);
            return;
        }

        string? startupPage = Environment.GetEnvironmentVariable("VEDABASE_STARTUP_PAGE");
        if (string.Equals(startupPage, "Bookmarks", StringComparison.OrdinalIgnoreCase))
        {
            NavView.SelectedItem = null;
            CreateNewTab("Bookmarks", "\uE8A4", typeof(BookmarksPage));
            return;
        }
        if (string.Equals(startupPage, "Highlights", StringComparison.OrdinalIgnoreCase))
        {
            NavView.SelectedItem = null;
            CreateNewTab("Highlights", "\uE7E6", typeof(HighlightsPage));
            return;
        }
        if (string.Equals(startupPage, "Search", StringComparison.OrdinalIgnoreCase))
        {
            NavView.SelectedItem = null;
            string? query = Environment.GetEnvironmentVariable("VEDABASE_STARTUP_SEARCH_QUERY") ?? "bhakti";
            CreateNewTab($"Search: {query}", "\uE721", typeof(SearchPage), query);
            return;
        }

        if (ContentTabs.TabItems.Count == 0)
        {
            CreateNewTab("Bhagavad-gītā", "\uE8A5", typeof(ReadingPage), "BG-1-1");
        }

        if (NavView.MenuItemsSource != null && ViewModel.Books.Any())
        {
            var firstBook = ViewModel.Books.FirstOrDefault(b => !b.IsFolder && !b.IsHeader)
                            ?? ViewModel.Books.FirstOrDefault()?.Children?.FirstOrDefault();
            if (firstBook != null)
            {
                NavView.SelectedItem = firstBook;
            }
        }
    }

    private void LibraryToggleButton_Click(object sender, RoutedEventArgs e)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
        LibraryToggleIcon.Glyph = "\uE700";
    }

    private void OpenRecentlyRead_Click(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = null;
        NavigateCurrentTab(typeof(RecentlyReadPage), null, "Recently Read", "\uE81C");
    }

    private void OpenBookmarks_Click(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = null;
        NavigateCurrentTab(typeof(BookmarksPage), null, "Bookmarks", "\uE8A4");
    }

    private void OpenNotes_Click(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = null;
        NavigateCurrentTab(typeof(NotesPage), null, "Notes", "\uE70B");
    }

    private void OpenHighlights_Click(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = null;
        NavigateCurrentTab(typeof(HighlightsPage), null, "Highlights", "\uE7E6");
    }

    public void OpenUserGuideTab()
    {
        NavView.SelectedItem = null;
        CreateNewTab("User Guide", "\uE897", typeof(HelpGuidePage));
    }

    private void UserGuideButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUserGuideTab();
    }

    private void HelpAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenUserGuideTab();
        args.Handled = true;
    }

    private void OpenUserGuide_Click(object sender, RoutedEventArgs e)
    {
        OpenUserGuideTab();
    }

    private async void OpenKeyboardShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Keyboard Shortcuts",
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot
        };
        VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

        var sp = new StackPanel { Spacing = 8, MaxWidth = 340 };
        void AddShortcut(string key, string desc)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var keyBlock = new TextBlock { Text = key, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var descBlock = new TextBlock { Text = desc };
            Grid.SetColumn(keyBlock, 0);
            Grid.SetColumn(descBlock, 1);
            row.Children.Add(keyBlock);
            row.Children.Add(descBlock);
            sp.Children.Add(row);
        }

        AddShortcut("Ctrl+Shift+L", "Toggle Light/Dark");
        AddShortcut("Ctrl+Shift+B", "Bookmark verse");
        AddShortcut("Ctrl+Shift+F", "Toggle Focus Mode");
        AddShortcut("Ctrl+Left / Right", "Previous / Next verse");
        AddShortcut("Ctrl + / - / 0", "Zoom In / Out / Reset");
        AddShortcut("Ctrl+K / Ctrl+F", "Focus search");
        AddShortcut("Ctrl+H", "Recently Read");
        AddShortcut("Ctrl+Shift+S", "Advanced Search (Folio)");
        AddShortcut("F1", "User Guide & Manual");
        AddShortcut("Escape", "Exit Focus / clear");

        dialog.Content = sp;
        await dialog.ShowAsync();
    }

    private async void OpenAdvancedSearch_Click(object sender, RoutedEventArgs e)
    {
        await OpenAdvancedSearchDialogAsync();
    }

    private async void AdvancedSearchAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await OpenAdvancedSearchDialogAsync();
    }

    public async Task OpenAdvancedSearchDialogAsync()
    {
        var dialog = new AdvancedSearchDialog(App.Current.Repository);
        VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);
        var res = await dialog.ShowAsync();
        if (res == ContentDialogResult.Primary || res == ContentDialogResult.Secondary)
        {
            string query = dialog.QueryResult;
            if (!string.IsNullOrWhiteSpace(query))
            {
                var navArgs = new SearchNavigationArgs
                {
                    Query = query,
                    CheckedBookKeys = dialog.GetCheckedBookKeys(),
                    FieldScope = dialog.SelectedScope,
                    AutoExecute = (res == ContentDialogResult.Primary)
                };
                CreateNewTab($"Search: {query}", "\uE721", typeof(SearchPage), navArgs);
            }
        }
    }

    public void OpenBookInActiveTab(BookNode book)
    {
        if (book.IsHeader || book.IsFolder) return;

        NavView.SelectedItem = book;

        if (book.IsPdf || (!string.IsNullOrEmpty(book.PdfPath) && File.Exists(book.PdfPath)))
        {
            NavigateCurrentTab(typeof(PdfViewerPage), book, book.Title, "\uE7C3");
            return;
        }

        // Reuse the already-hosted LibraryPage instance when switching
        // between books from the sidebar instead of tearing it down and
        // navigating to a brand-new one each time (LibraryPage doesn't
        // opt into Frame navigation caching, so every Navigate() call
        // was allocating a fresh page + ViewModel from scratch).
        if (CurrentFrame.Content is LibraryPage existingLibraryPage)
        {
            existingLibraryPage.ViewModel.LoadBook(book);
            if (ContentTabs.SelectedItem is TabViewItem tab)
            {
                tab.Header = book.Title;
            }
        }
        else
        {
            NavigateCurrentTab(typeof(LibraryPage), book, book.Title, "\uE8A5");
        }
    }

    private void BookRow_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var book = GetNodeFromSender(sender);
        if (book != null && !book.IsHeader && !book.IsFolder)
        {
            OpenBookInActiveTab(book);
            e.Handled = true;
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            NavigateCurrentTab(typeof(SettingsPage), null, "Settings", "\uE713");
            return;
        }
        if (args.InvokedItemContainer?.Tag is string tag && tag == "UserGuide")
        {
            OpenUserGuideTab();
            return;
        }
        if (args.InvokedItem is BookNode book)
        {
            OpenBookInActiveTab(book);
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateCurrentTab(typeof(SettingsPage), null, "Settings", "\uE713");
        }
        else if (args.SelectedItemContainer?.Tag is string tag && tag == "UserGuide")
        {
            OpenUserGuideTab();
        }
        else if (args.SelectedItem is BookNode book)
        {
            OpenBookInActiveTab(book);
        }
    }

    private async void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            if (DirectReferenceService.IsReferenceQuery(sender.Text))
            {
                try
                {
                    var suggestions = await App.Current.ReferenceService.GetSuggestionsAsync(sender.Text);
                    sender.ItemsSource = suggestions;
                }
                catch
                {
                    sender.ItemsSource = null;
                }
            }
            else
            {
                sender.ItemsSource = null;
            }
        }
    }

    private void AutoSuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is ReferenceSuggestion suggestion)
        {
            if (suggestion.RecordKey != null)
            {
                NavView.SelectedItem = null;
                NavigateCurrentTab(typeof(ReadingPage), suggestion.RecordKey, suggestion.RecordKey, "\uE8A5");
                sender.Text = string.Empty;
                sender.ItemsSource = null;
            }
            else
            {
                sender.Text = suggestion.QueryToComplete;
            }
        }
    }

    private async void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is ReferenceSuggestion chosen && chosen.RecordKey != null)
        {
            NavView.SelectedItem = null;
            NavigateCurrentTab(typeof(ReadingPage), chosen.RecordKey, chosen.RecordKey, "\uE8A5");
            sender.Text = string.Empty;
            sender.ItemsSource = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            if (DirectReferenceService.IsReferenceQuery(args.QueryText))
            {
                var recordKey = await App.Current.ReferenceService.TryResolveExactAsync(args.QueryText);
                if (recordKey != null)
                {
                    NavView.SelectedItem = null;
                    NavigateCurrentTab(typeof(ReadingPage), recordKey, recordKey, "\uE8A5");
                    sender.Text = string.Empty;
                    sender.ItemsSource = null;
                    return;
                }
            }

            NavView.SelectedItem = null; // Deselect library items
            NavigateCurrentTab(typeof(SearchPage), args.QueryText, $"Search: {args.QueryText}", "\uE721");
        }
    }

    private void RecentlyReadAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        NavView.SelectedItem = null;
        NavigateCurrentTab(typeof(RecentlyReadPage), null, "Recently Read", "\uE81C");
        args.Handled = true;
    }

    private void SearchAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        NavView.AutoSuggestBox?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        args.Handled = true;
    }

    private bool _isAppFocusModeActive;

    public void EnterFocusMode(ReadingPage readingPage)
    {
        if (_isAppFocusModeActive) return;
        _isAppFocusModeActive = true;

        AppTopBar.Visibility = Visibility.Collapsed;
        NavView.IsPaneVisible = false;
        SetTabStripVisibility(false);
    }

    public void ExitFocusMode()
    {
        if (!_isAppFocusModeActive) return;
        _isAppFocusModeActive = false;

        AppTopBar.Visibility = Visibility.Visible;
        NavView.IsPaneVisible = true;
        SetTabStripVisibility(true);
    }

    private void SetTabStripVisibility(bool visible)
    {
        try
        {
            ContentTabs.IsAddTabButtonVisible = visible;

            if (VisualTreeHelper.GetChildrenCount(ContentTabs) > 0)
            {
                var root = VisualTreeHelper.GetChild(ContentTabs, 0);
                if (root is Grid rootGrid)
                {
                    if (rootGrid.RowDefinitions.Count >= 2)
                    {
                        rootGrid.RowDefinitions[0].Height = visible ? GridLength.Auto : new GridLength(0);
                        rootGrid.RowDefinitions[0].MaxHeight = visible ? double.PositiveInfinity : 0;
                        rootGrid.RowDefinitions[0].MinHeight = 0;
                    }

                    int childCount = VisualTreeHelper.GetChildrenCount(rootGrid);
                    for (int i = 0; i < childCount; i++)
                    {
                        var child = VisualTreeHelper.GetChild(rootGrid, i);
                        if (child is FrameworkElement fe && Grid.GetRow(fe) == 0)
                        {
                            fe.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                            fe.Height = visible ? double.NaN : 0;
                            fe.MaxHeight = visible ? double.PositiveInfinity : 0;
                            fe.MinHeight = 0;
                        }
                    }
                }
            }

            CollapseOrShowTabStripElements(ContentTabs, visible);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FocusMode] Error toggling TabStrip: {ex}");
        }
    }

    private static void CollapseOrShowTabStripElements(DependencyObject parent, bool visible)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe)
            {
                if (fe.Name == "TabStrip" || fe.Name == "TabListView" || fe.Name == "TabContentGrid" || fe.Name == "TabContainerGrid")
                {
                    if (fe is Grid g && g.RowDefinitions.Count >= 2)
                    {
                        g.RowDefinitions[0].Height = visible ? GridLength.Auto : new GridLength(0);
                        g.RowDefinitions[0].MaxHeight = visible ? double.PositiveInfinity : 0;
                    }
                }
                if (fe.Name == "TabListView" || fe is ListView)
                {
                    fe.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    fe.MaxHeight = visible ? double.PositiveInfinity : 0;
                }
            }
            CollapseOrShowTabStripElements(child, visible);
        }
    }

    public void ToggleLibraryPane()
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    public bool IsLibraryPaneOpen
    {
        get => NavView.IsPaneOpen;
        set => NavView.IsPaneOpen = value;
    }

    private void EscapeAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isAppFocusModeActive)
        {
            if (CurrentFrame?.Content is ReadingPage rp)
            {
                rp.ExitZenMode();
            }
            else
            {
                ExitFocusMode();
            }
            args.Handled = true;
        }
    }

    private async void RemoveBookContextItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is BookNode book)
        {
            if (App.Current.BookImportService != null && App.Current.BookImportService.IsBookProtected(book.BookKey))
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Remove Book from Library",
                Content = $"Are you sure you want to remove '{book.Title}' from your Library? Any custom notes and highlights on this book will remain in your personal workspace.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary && App.Current.BookImportService != null)
            {
                await App.Current.BookImportService.DeleteBookAsync(book.BookKey);
                await ViewModel.LoadBooksAsync(true);
            }
        }
    }

    public Visibility CountVis(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

    private void FocusModeAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CurrentFrame?.Content is ReadingPage rp)
        {
            rp.ToggleZenMode();
            args.Handled = true;
        }
    }

    private Windows.Foundation.Point _pointerStartPos;
    private bool _isPointerPressed;
    private FrameworkElement? _dragSourceElement;

    private void ItemRow_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var pt = e.GetCurrentPoint(fe);
            if (pt.Properties.IsLeftButtonPressed)
            {
                _isPointerPressed = true;
                _pointerStartPos = pt.Position;
                _dragSourceElement = fe;
            }
        }
    }

    private async void ItemRow_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isPointerPressed && _dragSourceElement != null && ReferenceEquals(sender, _dragSourceElement))
        {
            var pt = e.GetCurrentPoint(_dragSourceElement);
            if (pt.Properties.IsLeftButtonPressed)
            {
                var diffX = Math.Abs(pt.Position.X - _pointerStartPos.X);
                var diffY = Math.Abs(pt.Position.Y - _pointerStartPos.Y);
                if (diffX > 6 || diffY > 6)
                {
                    _isPointerPressed = false;
                    var el = _dragSourceElement;
                    _dragSourceElement = null;
                    try
                    {
                        await el.StartDragAsync(pt);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DragDrop] StartDragAsync: {ex.Message}");
                    }
                }
            }
            else
            {
                _isPointerPressed = false;
                _dragSourceElement = null;
            }
        }
    }

    private void ItemRow_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isPointerPressed = false;
        _dragSourceElement = null;
    }

    private void ItemRow_PointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isPointerPressed = false;
        _dragSourceElement = null;
    }

    private BookNode? GetNodeFromSender(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is BookNode b) return b;
            if (fe.DataContext is BookNode b2) return b2;
        }
        return null;
    }

    private void BookNavItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var book = GetNodeFromSender(sender);
        if (book != null && !string.IsNullOrEmpty(book.BookKey))
        {
            args.Data.SetText(book.BookKey);
            args.Data.Properties["BookKey"] = book.BookKey;
            args.Data.Properties["BookTitle"] = book.Title;
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.DragUI.SetContentFromDataPackage();
        }
    }

    private void FolderNavItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var folder = GetNodeFromSender(sender);
        if (folder != null && folder.IsFolder)
        {
            args.Data.SetText(folder.Title);
            args.Data.Properties["FolderTitle"] = folder.Title;
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.DragUI.SetContentFromDataPackage();
        }
    }

    private void FolderNavItem_DragOver(object sender, DragEventArgs e)
    {
        var targetFolder = GetNodeFromSender(sender);
        if (targetFolder == null || !targetFolder.IsFolder)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (e.DataView.Properties.ContainsKey("FolderTitle"))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"Move folder before {targetFolder.Title}";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }
        else if (e.DataView.Properties.ContainsKey("BookKey") || e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"Move into {targetFolder.Title}";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void FolderNavItem_Drop(object sender, DragEventArgs e)
    {
        var targetFolder = GetNodeFromSender(sender);
        if (targetFolder == null || !targetFolder.IsFolder) return;

        if (e.DataView.Properties.TryGetValue("FolderTitle", out var fVal) && fVal is string draggedFolderTitle)
        {
            if (!string.Equals(draggedFolderTitle, targetFolder.Title, StringComparison.OrdinalIgnoreCase))
            {
                await ReorderFolderAsync(draggedFolderTitle, targetFolder.Title);
            }
            return;
        }

        string? draggedBookKey = null;
        if (e.DataView.Properties.TryGetValue("BookKey", out var val) && val is string s)
        {
            draggedBookKey = s;
        }
        else if (e.DataView.Contains(StandardDataFormats.Text))
        {
            draggedBookKey = await e.DataView.GetTextAsync();
        }

        if (!string.IsNullOrEmpty(draggedBookKey))
        {
            await MoveBookToFolderAsync(draggedBookKey, targetFolder.Title);
        }
    }

    private void BookNavItem_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.ContainsKey("BookKey") || e.DataView.Contains(StandardDataFormats.Text))
        {
            var targetBook = GetNodeFromSender(sender);
            if (targetBook != null && !targetBook.IsFolder && !string.IsNullOrEmpty(targetBook.BookKey))
            {
                e.AcceptedOperation = DataPackageOperation.Move;
                e.DragUIOverride.Caption = $"Move next to {targetBook.Title}";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsContentVisible = true;
                return;
            }
        }
        e.AcceptedOperation = DataPackageOperation.None;
    }

    private async void BookNavItem_Drop(object sender, DragEventArgs e)
    {
        var targetBook = GetNodeFromSender(sender);
        if (targetBook == null || targetBook.IsFolder || string.IsNullOrEmpty(targetBook.BookKey)) return;

        string? draggedBookKey = null;
        if (e.DataView.Properties.TryGetValue("BookKey", out var val) && val is string s)
        {
            draggedBookKey = s;
        }
        else if (e.DataView.Contains(StandardDataFormats.Text))
        {
            draggedBookKey = await e.DataView.GetTextAsync();
        }

        if (!string.IsNullOrEmpty(draggedBookKey))
        {
            if (!string.Equals(draggedBookKey, targetBook.BookKey, StringComparison.OrdinalIgnoreCase))
            {
                await ReorderOrMoveBookAsync(draggedBookKey, targetBook.BookKey);
            }
        }
    }

    private async void MoveBookUpContextItem_Click(object sender, RoutedEventArgs e)
    {
        var book = GetNodeFromSender(sender);
        if (book != null && !string.IsNullOrEmpty(book.BookKey))
        {
            await MoveBookRelativeAsync(book.BookKey, -1);
        }
    }

    private async void MoveBookDownContextItem_Click(object sender, RoutedEventArgs e)
    {
        var book = GetNodeFromSender(sender);
        if (book != null && !string.IsNullOrEmpty(book.BookKey))
        {
            await MoveBookRelativeAsync(book.BookKey, 1);
        }
    }

    private async void MoveBookToSPFolder_Click(object sender, RoutedEventArgs e)
    {
        var book = GetNodeFromSender(sender);
        if (book != null && !string.IsNullOrEmpty(book.BookKey))
        {
            await MoveBookToFolderAsync(book.BookKey, "Śrīla Prabhupāda's Works");
        }
    }

    private async void MoveBookToOtherFolder_Click(object sender, RoutedEventArgs e)
    {
        var book = GetNodeFromSender(sender);
        if (book != null && !string.IsNullOrEmpty(book.BookKey))
        {
            await MoveBookToFolderAsync(book.BookKey, "Works by Other Ācāryas & Authors");
        }
    }

    private async void MoveFolderUpContextItem_Click(object sender, RoutedEventArgs e)
    {
        var folder = GetNodeFromSender(sender);
        if (folder != null && folder.IsFolder)
        {
            await MoveFolderRelativeAsync(folder.Title, -1);
        }
    }

    private async void MoveFolderDownContextItem_Click(object sender, RoutedEventArgs e)
    {
        var folder = GetNodeFromSender(sender);
        if (folder != null && folder.IsFolder)
        {
            await MoveFolderRelativeAsync(folder.Title, 1);
        }
    }

    private async void SortFolderAlphabeticalContextItem_Click(object sender, RoutedEventArgs e)
    {
        var node = GetNodeFromSender(sender);
        if (node == null) return;

        BookNode? folder = node.IsFolder
            ? node
            : ViewModel.Books.FirstOrDefault(f => f.Children != null && f.Children.Any(b => b.BookKey.Equals(node.BookKey, StringComparison.OrdinalIgnoreCase)));

        if (folder != null && folder.Children != null)
        {
            var bookKeys = folder.Children.Select(b => b.BookKey).ToList();
            await App.Current.UserRepository.ResetBookDisplayOrderForKeysAsync(bookKeys);
            (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
            await ViewModel.LoadBooksAsync(true);
        }
    }

    private async Task MoveBookRelativeAsync(string bookKey, int delta)
    {
        var folder = ViewModel.Books.FirstOrDefault(f => f.Children != null && f.Children.Any(b => b.BookKey.Equals(bookKey, StringComparison.OrdinalIgnoreCase)));
        if (folder == null || folder.Children == null) return;

        var list = folder.Children.ToList();
        int idx = list.FindIndex(b => b.BookKey.Equals(bookKey, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;

        int newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= list.Count) return;

        var node = list[idx];
        list.RemoveAt(idx);
        list.Insert(newIdx, node);

        var orderMap = new Dictionary<string, int>();
        for (int i = 0; i < list.Count; i++)
        {
            orderMap[list[i].BookKey] = (i + 1) * 10;
        }

        await App.Current.UserRepository.SetBooksDisplayOrderAsync(orderMap);
        (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
        await ViewModel.LoadBooksAsync(true);
    }

    private async Task MoveFolderRelativeAsync(string folderTitle, int delta)
    {
        var currentFolders = ViewModel.Books.Where(b => b.IsFolder).Select(b => b.Title).ToList();
        int idx = currentFolders.FindIndex(f => f.Equals(folderTitle, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;

        int newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= currentFolders.Count) return;

        var item = currentFolders[idx];
        currentFolders.RemoveAt(idx);
        currentFolders.Insert(newIdx, item);

        await App.Current.UserRepository.SetFolderDisplayOrderAsync(currentFolders);
        await ViewModel.LoadBooksAsync(true);
    }

    private async Task ReorderFolderAsync(string draggedFolderTitle, string targetFolderTitle)
    {
        var currentFolders = ViewModel.Books.Where(b => b.IsFolder).Select(b => b.Title).ToList();
        int srcIdx = currentFolders.FindIndex(f => f.Equals(draggedFolderTitle, StringComparison.OrdinalIgnoreCase));
        int dstIdx = currentFolders.FindIndex(f => f.Equals(targetFolderTitle, StringComparison.OrdinalIgnoreCase));
        if (srcIdx >= 0 && dstIdx >= 0 && srcIdx != dstIdx)
        {
            var item = currentFolders[srcIdx];
            currentFolders.RemoveAt(srcIdx);
            currentFolders.Insert(dstIdx, item);

            await App.Current.UserRepository.SetFolderDisplayOrderAsync(currentFolders);
            await ViewModel.LoadBooksAsync(true);
        }
    }

    private async Task MoveBookToFolderAsync(string bookKey, string targetFolderTitle)
    {
        if (targetFolderTitle.Equals("Śrīla Prabhupāda's Works", StringComparison.OrdinalIgnoreCase))
        {
            var hierarchy = await App.Current.Repository.GetLibraryHierarchyAsync();
            var origBook = hierarchy.FirstOrDefault(b => b.BookKey.Equals(bookKey, StringComparison.OrdinalIgnoreCase));
            if (origBook != null && !origBook.IsOtherAuthor)
            {
                await App.Current.UserRepository.RemoveBookCategoryOverrideAsync(bookKey);
            }
            else
            {
                await App.Current.UserRepository.SetBookCategoryOverrideAsync(bookKey, "ŚRĪLA PRABHUPĀDA'S WORKS");
            }
        }
        else if (targetFolderTitle.Equals("Works by Other Ācāryas & Authors", StringComparison.OrdinalIgnoreCase))
        {
            await App.Current.UserRepository.SetBookCategoryOverrideAsync(bookKey, "WORKS BY OTHER ĀCĀRYAS & AUTHORS");
        }
        else
        {
            await App.Current.UserRepository.SetBookCategoryOverrideAsync(bookKey, targetFolderTitle);
        }

        (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
        await ViewModel.LoadBooksAsync(true);
    }

    private async Task ReorderOrMoveBookAsync(string draggedBookKey, string targetBookKey)
    {
        BookNode? targetFolder = ViewModel.Books.FirstOrDefault(f => f.Children != null && f.Children.Any(b => b.BookKey.Equals(targetBookKey, StringComparison.OrdinalIgnoreCase)));
        BookNode? sourceFolder = ViewModel.Books.FirstOrDefault(f => f.Children != null && f.Children.Any(b => b.BookKey.Equals(draggedBookKey, StringComparison.OrdinalIgnoreCase)));

        if (targetFolder == null || targetFolder.Children == null) return;

        if (sourceFolder != null && !string.Equals(sourceFolder.Title, targetFolder.Title, StringComparison.OrdinalIgnoreCase))
        {
            // Moving into target folder first
            await MoveBookToFolderAsync(draggedBookKey, targetFolder.Title);
            // Re-find target folder after reload
            targetFolder = ViewModel.Books.FirstOrDefault(f => f.Children != null && f.Children.Any(b => b.BookKey.Equals(targetBookKey, StringComparison.OrdinalIgnoreCase)));
            if (targetFolder == null || targetFolder.Children == null) return;
        }

        var list = targetFolder.Children.ToList();
        var draggedNode = list.FirstOrDefault(b => b.BookKey.Equals(draggedBookKey, StringComparison.OrdinalIgnoreCase));
        if (draggedNode != null)
        {
            list.Remove(draggedNode);
            int targetIdx = list.FindIndex(b => b.BookKey.Equals(targetBookKey, StringComparison.OrdinalIgnoreCase));
            if (targetIdx >= 0)
            {
                list.Insert(targetIdx, draggedNode);
            }
            else
            {
                list.Add(draggedNode);
            }

            var orderMap = new Dictionary<string, int>();
            for (int i = 0; i < list.Count; i++)
            {
                orderMap[list[i].BookKey] = (i + 1) * 10;
            }

            await App.Current.UserRepository.SetBooksDisplayOrderAsync(orderMap);
            (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
            await ViewModel.LoadBooksAsync(true);
        }
    }
}
