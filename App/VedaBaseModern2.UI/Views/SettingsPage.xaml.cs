using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VedaBaseModern.Core.Models;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            this.InitializeComponent();
            ViewModel = new SettingsViewModel(App.Current.SettingsService, theme =>
            {
                App.Current.MainWindowInstance?.ApplyTheme(theme);
            });
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadAsync();
            await LoadCorpusManagementDataAsync();
        }

        private void OpenUserGuide_Click(object sender, RoutedEventArgs e)
        {
            MainPage.Current?.CreateNewTab("User Guide", "\uE897", typeof(HelpGuidePage));
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ResetToDefaultsCommand.ExecuteAsync(null);
            ResetFlyoutButton.Flyout?.Hide();
        }

        private async void ExportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("VedaBase Research Backup Archive", new List<string> { ".vdbbackup" });
                picker.FileTypeChoices.Add("JSON Backup", new List<string> { ".json" });
                picker.SuggestedFileName = $"VedaBase_Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

                var hwnd = App.Current.MainWindowInstance?.WindowHandle ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await ViewModel.ExportBackupAsync(file.Path);
                }
            }
            catch (Exception ex)
            {
                ViewModel.BackupErrorMessage = $"Export picker error: {ex.Message}";
            }
        }

        private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".vdbbackup");
                picker.FileTypeFilter.Add(".json");

                var hwnd = App.Current.MainWindowInstance?.WindowHandle ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                // Inspect archive before prompting
                var validation = await ViewModel.InspectBackupAsync(file.Path);
                if (!validation.IsValid || validation.Manifest == null)
                {
                    ViewModel.BackupErrorMessage = validation.ErrorMessage ?? "Invalid backup file.";
                    return;
                }

                // Prompt user with ContentDialog showing counts and Merge vs Replace
                var dialog = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "Restore Research Data",
                    PrimaryButtonText = "Restore",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };
                VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

                var m = validation.Manifest;
                var contentPanel = new StackPanel { Spacing = 12 };
                string devShort = m.DeviceId.Length >= 8 ? m.DeviceId[..8] : m.DeviceId;
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"Backup from {m.ExportedUtc.ToLocalTime():g} (Device: {devShort}...)",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });

                var countsBlock = new TextBlock
                {
                    Text = $"Contains: {m.Counts.Bookmarks} bookmarks, {m.Counts.Highlights} highlights, {m.Counts.Notes} notes, {m.Counts.Collections} collections.",
                    Foreground = VedaBaseModern.UI.Services.CustomThemeService.GetThemeBrush("TextFillColorSecondaryBrush", dialog.RequestedTheme)
                };
                contentPanel.Children.Add(countsBlock);

                var radioGroup = new RadioButtons
                {
                    Header = "Restore Strategy",
                    SelectedIndex = 0
                };
                radioGroup.Items.Add(new RadioButton { Content = "Merge (Recommended) — Combines backup with existing data using Last-Write-Wins" });
                radioGroup.Items.Add(new RadioButton { Content = "Replace (Clean Restore) — Erases current personal research data and restores from backup" });
                contentPanel.Children.Add(radioGroup);

                var snapshotNoteBlock = new TextBlock
                {
                    Text = "Note: An automated safety snapshot of your current database will be saved before restoring.",
                    FontSize = 12,
                    Foreground = VedaBaseModern.UI.Services.CustomThemeService.GetThemeBrush("TextFillColorSecondaryBrush", dialog.RequestedTheme)
                };
                contentPanel.Children.Add(snapshotNoteBlock);

                dialog.Content = contentPanel;

                var dialogResult = await dialog.ShowAsync();
                if (dialogResult == ContentDialogResult.Primary)
                {
                    var mode = radioGroup.SelectedIndex == 1 ? BackupImportMode.Replace : BackupImportMode.Merge;
                    await ViewModel.RestoreBackupAsync(file.Path, mode);
                }
            }
            catch (Exception ex)
            {
                ViewModel.BackupErrorMessage = $"Restore error: {ex.Message}";
            }
        }

        private async void SyncNowButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.SyncNowAsync();
        }

        private async void ConnectSupabaseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Cloud Sync Account",
                PrimaryButtonText = "Sign In",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

            var rootPanel = new StackPanel { Spacing = 14, MaxWidth = 440 };

            // Segmented mode switcher
            var modeSelector = new RadioButtons
            {
                Header = "Account Action",
                SelectedIndex = 0,
                MaxColumns = 3
            };
            modeSelector.Items.Add("Sign In");
            modeSelector.Items.Add("Create Account");
            modeSelector.Items.Add("Forgot Password");
            rootPanel.Children.Add(modeSelector);

            // Intro text
            var introText = new TextBlock
            {
                Text = "Sign in to synchronize your bookmarks, highlights, and notes across all your devices.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = VedaBaseModern.UI.Services.CustomThemeService.GetThemeBrush("TextFillColorSecondaryBrush", dialog.RequestedTheme)
            };
            rootPanel.Children.Add(introText);

            // Input Fields
            var emailBox = new TextBox
            {
                Header = "Email Address",
                PlaceholderText = "devotee@example.com",
                Text = !string.IsNullOrWhiteSpace(ViewModel.UserEmailDisplay) && ViewModel.UserEmailDisplay.Contains('@')
                    ? ViewModel.UserEmailDisplay
                    : string.Empty
            };
            rootPanel.Children.Add(emailBox);

            var passBox = new PasswordBox
            {
                Header = "Password",
                PlaceholderText = "Enter your password"
            };
            rootPanel.Children.Add(passBox);

            var confirmPassBox = new PasswordBox
            {
                Header = "Confirm Password",
                PlaceholderText = "Re-enter your password",
                Visibility = Visibility.Collapsed
            };
            rootPanel.Children.Add(confirmPassBox);

            // Error display inside dialog
            var dialogErrorText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Visibility = Visibility.Collapsed,
                Foreground = VedaBaseModern.UI.Services.CustomThemeService.GetThemeBrush("SystemFillColorCriticalBrush", dialog.RequestedTheme)
            };
            rootPanel.Children.Add(dialogErrorText);

            // Advanced Expander for custom self-hosted Supabase server
            var customServerExpander = new Expander
            {
                Header = "Advanced: Custom Server",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsExpanded = false
            };
            var customServerPanel = new StackPanel { Spacing = 8, Padding = new Thickness(0, 8, 0, 0) };
            var customServerDesc = new TextBlock
            {
                Text = "By default, Prabhupāda Connect uses the official cloud sync backend. Power users can optionally connect their self-hosted Supabase instance.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = VedaBaseModern.UI.Services.CustomThemeService.GetThemeBrush("TextFillColorSecondaryBrush", dialog.RequestedTheme)
            };
            customServerPanel.Children.Add(customServerDesc);
            var urlBox = new TextBox
            {
                Header = "Project URL",
                PlaceholderText = "https://your-project.supabase.co",
                Text = ViewModel.SupabaseProjectUrlDisplay ?? string.Empty
            };
            customServerPanel.Children.Add(urlBox);
            var keyBox = new PasswordBox
            {
                Header = "Anon / Public Key",
                PlaceholderText = "eyJhbGciOiJIUzI1NiIsInR..."
            };
            customServerPanel.Children.Add(keyBox);
            customServerExpander.Content = customServerPanel;
            rootPanel.Children.Add(customServerExpander);

            // Handle mode selection change
            modeSelector.SelectionChanged += (s, args) =>
            {
                dialogErrorText.Visibility = Visibility.Collapsed;
                dialogErrorText.Text = string.Empty;

                switch (modeSelector.SelectedIndex)
                {
                    case 0: // Sign In
                        dialog.PrimaryButtonText = "Sign In";
                        introText.Text = "Sign in to synchronize your bookmarks, highlights, and notes across all your devices.";
                        passBox.Visibility = Visibility.Visible;
                        confirmPassBox.Visibility = Visibility.Collapsed;
                        break;
                    case 1: // Create Account
                        dialog.PrimaryButtonText = "Create Account";
                        introText.Text = "Create a free account to back up and sync your research annotations across devices.";
                        passBox.Visibility = Visibility.Visible;
                        confirmPassBox.Visibility = Visibility.Visible;
                        break;
                    case 2: // Forgot Password
                        dialog.PrimaryButtonText = "Send Reset Link";
                        introText.Text = "Enter your registered email address and we'll send you a link to reset your password.";
                        passBox.Visibility = Visibility.Collapsed;
                        confirmPassBox.Visibility = Visibility.Collapsed;
                        break;
                }
            };

            dialog.Content = rootPanel;

            // Handle primary button click with validation and async execution
            dialog.PrimaryButtonClick += async (d, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    dialogErrorText.Visibility = Visibility.Collapsed;
                    string email = emailBox.Text.Trim();
                    string pass = passBox.Password;
                    string confirmPass = confirmPassBox.Password;
                    string? customUrl = string.IsNullOrWhiteSpace(urlBox.Text) ? null : urlBox.Text.Trim();
                    string? customKey = string.IsNullOrWhiteSpace(keyBox.Password) ? null : keyBox.Password.Trim();

                    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                    {
                        dialogErrorText.Text = "Please enter a valid email address.";
                        dialogErrorText.Visibility = Visibility.Visible;
                        args.Cancel = true;
                        return;
                    }

                    if (modeSelector.SelectedIndex == 1) // Create Account
                    {
                        if (string.IsNullOrEmpty(pass) || pass.Length < 6)
                        {
                            dialogErrorText.Text = "Password must be at least 6 characters long.";
                            dialogErrorText.Visibility = Visibility.Visible;
                            args.Cancel = true;
                            return;
                        }

                        if (pass != confirmPass)
                        {
                            dialogErrorText.Text = "Passwords do not match. Please re-enter.";
                            dialogErrorText.Visibility = Visibility.Visible;
                            args.Cancel = true;
                            return;
                        }

                        var result = await ViewModel.SignUpAsync(email, pass, customUrl, customKey);
                        if (!result.Success)
                        {
                            dialogErrorText.Text = result.Message;
                            dialogErrorText.Visibility = Visibility.Visible;
                            args.Cancel = true;
                            return;
                        }
                    }
                    else if (modeSelector.SelectedIndex == 0) // Sign In
                    {
                        if (string.IsNullOrEmpty(pass))
                        {
                            dialogErrorText.Text = "Please enter your password.";
                            dialogErrorText.Visibility = Visibility.Visible;
                            args.Cancel = true;
                            return;
                        }

                        var result = await ViewModel.SignInAsync(email, pass, customUrl, customKey);
                        if (!result.Success)
                        {
                            dialogErrorText.Text = result.Message;
                            dialogErrorText.Visibility = Visibility.Visible;
                            args.Cancel = true;
                            return;
                        }
                    }
                    else if (modeSelector.SelectedIndex == 2) // Forgot Password
                    {
                        var result = await ViewModel.SendPasswordResetEmailAsync(email, customUrl, customKey);
                        if (!result.Success)
                        {
                            dialogErrorText.Text = result.Message;
                            dialogErrorText.Visibility = Visibility.Visible;
                            args.Cancel = true;
                            return;
                        }
                    }
                }
                finally
                {
                    deferral.Complete();
                }
            };

            await dialog.ShowAsync();
        }

        private async void CreateSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.CreateSafetySnapshotAsync();
        }

        private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("Sync Diagnostics (JSON)", new List<string> { ".json" });
                picker.SuggestedFileName = $"VedaBase_SyncDiagnostics_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

                var hwnd = App.Current.MainWindowInstance?.WindowHandle ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await ViewModel.ExportSyncDiagnosticsAsync(file.Path);
                }
            }
            catch (Exception ex)
            {
                ViewModel.DiagnosticsStatusMessage = $"Export picker error: {ex.Message}";
            }
        }

        private async void DisconnectSupabaseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Disconnect Cloud Sync",
                Content = "Are you sure you want to disconnect from your Supabase project? All your personal notes, highlights, and bookmarks will remain saved locally on this machine.",
                PrimaryButtonText = "Disconnect",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DisconnectSupabaseAsync();
            }
        }

        public class ImportedBookDisplayItem
        {
            public string BookKey { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Author { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public bool IsPdf { get; set; }
            public string FormatBadge => IsPdf ? "PDF Book" : "JSON Corpus";
            public string IconGlyph => IsPdf ? "\uEA90" : "\uE82D";
        }

        public class FolderDisplayItem
        {
            public string Name { get; set; } = string.Empty;
            public bool CanDelete { get; set; }
            public Visibility DeleteButtonVisibility => CanDelete ? Visibility.Visible : Visibility.Collapsed;
        }

        public class BookFolderAssignmentItem
        {
            public string BookKey { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string EffectiveFolder { get; set; } = string.Empty;
            public List<string> FolderOptions { get; set; } = new();
            public int SelectedFolderIndex { get; set; }
        }

        private bool _suppressCategoryAssignmentUpdate;
        private List<FolderDisplayItem> _folderDisplayItems = new();
        private List<BookFolderAssignmentItem> _allAssignmentItems = new();
        private string? _currentlySelectedFolderToOrganize;

        private async Task LoadCorpusManagementDataAsync()
        {
            try
            {
                // 1. Load imported books
                var importedBooks = await App.Current.BookImportService.GetImportedBooksAsync();
                var displayList = importedBooks.Select(b => new ImportedBookDisplayItem
                {
                    BookKey = b.BookKey,
                    Title = b.Title,
                    Author = string.IsNullOrWhiteSpace(b.Author) ? "Vaiṣṇava Ācārya" : b.Author,
                    Category = string.IsNullOrWhiteSpace(b.Category) ? "Uncategorized" : b.Category,
                    IsPdf = b.IsPdf
                }).ToList();

                ImportedBooksListView.ItemsSource = displayList;
                NoImportedBooksText.Visibility = displayList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // 2. Load folders (built-in + custom) and their display order
                var customFolders = await App.Current.UserRepository.GetCustomFoldersAsync();
                var savedFolderOrder = await App.Current.UserRepository.GetFolderDisplayOrderAsync();

                var allFolderNames = new List<string> { "Śrīla Prabhupāda's Works" };
                allFolderNames.AddRange(customFolders);
                if (!allFolderNames.Contains("Works by Other Ācāryas & Authors"))
                {
                    allFolderNames.Add("Works by Other Ācāryas & Authors");
                }

                int GetFolderPriority(string folderName)
                {
                    int idx = savedFolderOrder.FindIndex(f => f.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) return idx;
                    if (folderName.Equals("Śrīla Prabhupāda's Works", StringComparison.OrdinalIgnoreCase)) return -100;
                    if (folderName.Equals("Works by Other Ācāryas & Authors", StringComparison.OrdinalIgnoreCase)) return 1000;
                    return 0;
                }

                allFolderNames = allFolderNames
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => GetFolderPriority(f))
                    .ToList();

                _folderDisplayItems = allFolderNames.Select(name => new FolderDisplayItem
                {
                    Name = name,
                    CanDelete = !name.Equals("Śrīla Prabhupāda's Works", StringComparison.OrdinalIgnoreCase) &&
                                !name.Equals("Works by Other Ācāryas & Authors", StringComparison.OrdinalIgnoreCase)
                }).ToList();

                FoldersListView.ItemsSource = _folderDisplayItems;

                // 3. Load books and category overrides
                var allBooks = await App.Current.Repository.GetLibraryHierarchyAsync();
                var overrides = await App.Current.UserRepository.GetBookCategoryOverridesAsync();

                var folderOptions = new List<string> { "(Default Folder)" };
                folderOptions.AddRange(allFolderNames);

                _suppressCategoryAssignmentUpdate = true;
                _allAssignmentItems = new List<BookFolderAssignmentItem>();

                foreach (var b in allBooks)
                {
                    if (b.BookKey == "DI" || b.BookKey == "MADHYA" || b.BookKey == "ANTYA") continue;

                    string effectiveFolder;
                    if (overrides.TryGetValue(b.BookKey, out var userCat) &&
                        !string.IsNullOrWhiteSpace(userCat) &&
                        !userCat.Equals("(Default Folder)", StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveFolder = userCat.Trim();
                    }
                    else if (!b.IsOtherAuthor)
                    {
                        effectiveFolder = "Śrīla Prabhupāda's Works";
                    }
                    else
                    {
                        effectiveFolder = "Works by Other Ācāryas & Authors";
                    }

                    string assignedFolderChoice = overrides.TryGetValue(b.BookKey, out var cf) && !string.IsNullOrWhiteSpace(cf)
                        ? cf.Trim()
                        : "(Default Folder)";

                    int selIdx = folderOptions.FindIndex(o => o.Equals(assignedFolderChoice, StringComparison.OrdinalIgnoreCase));
                    if (selIdx < 0)
                    {
                        folderOptions.Add(assignedFolderChoice);
                        selIdx = folderOptions.Count - 1;
                    }

                    _allAssignmentItems.Add(new BookFolderAssignmentItem
                    {
                        BookKey = b.BookKey,
                        Title = b.Title,
                        EffectiveFolder = effectiveFolder,
                        FolderOptions = new List<string>(folderOptions),
                        SelectedFolderIndex = selIdx >= 0 ? selIdx : 0
                    });
                }

                // Setup Folder selector for organizing books
                SelectedFolderToOrganizeComboBox.ItemsSource = allFolderNames;
                if (string.IsNullOrEmpty(_currentlySelectedFolderToOrganize) || !allFolderNames.Contains(_currentlySelectedFolderToOrganize))
                {
                    _currentlySelectedFolderToOrganize = allFolderNames.FirstOrDefault() ?? "Śrīla Prabhupāda's Works";
                }
                SelectedFolderToOrganizeComboBox.SelectedItem = _currentlySelectedFolderToOrganize;

                await RefreshFolderBooksListAsync();
                _suppressCategoryAssignmentUpdate = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsPage] Error loading corpus management data: {ex}");
            }
        }

        private async void ImportPdfSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ImportPdfDialog { XamlRoot = this.XamlRoot };
                VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    BookImportStatusText.Text = $"Importing PDF '{dialog.BookTitle}'...";
                    BookImportStatusText.Visibility = Visibility.Visible;

                    var importResult = await App.Current.BookImportService.ImportPdfBookAsync(
                        dialog.SelectedFilePath, dialog.BookTitle, dialog.Author, dialog.Category);

                    BookImportStatusText.Text = importResult.Message;
                    if (importResult.Success)
                    {
                        (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
                        if (MainPage.Current != null)
                        {
                            await MainPage.Current.ViewModel.LoadBooksAsync(true);
                        }
                        await LoadCorpusManagementDataAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                BookImportStatusText.Text = $"PDF Import error: {ex.Message}";
                BookImportStatusText.Visibility = Visibility.Visible;
            }
        }

        private async void RemoveImportedBook_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string bookKey)
            {
                var dialog = new ContentDialog
                {
                    Title = "Remove Book from Library",
                    Content = "Are you sure you want to remove this imported book from your library? Its records and local files will be deleted.",
                    PrimaryButtonText = "Remove",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

                var res = await dialog.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    bool deleted = await App.Current.BookImportService.DeleteBookAsync(bookKey);
                    if (deleted)
                    {
                        await App.Current.UserRepository.RemoveBookCategoryOverrideAsync(bookKey);
                        (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
                        if (MainPage.Current != null)
                        {
                            await MainPage.Current.ViewModel.LoadBooksAsync(true);
                        }
                        await LoadCorpusManagementDataAsync();
                        BookImportStatusText.Text = "Book removed successfully.";
                        BookImportStatusText.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private async void AddCustomFolder_Click(object sender, RoutedEventArgs e)
        {
            string name = NewFolderNameBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(name))
            {
                await App.Current.UserRepository.AddCustomFolderAsync(name);
                NewFolderNameBox.Text = "";
                await LoadCorpusManagementDataAsync();
                if (MainPage.Current != null)
                {
                    await MainPage.Current.ViewModel.LoadBooksAsync(true);
                }
            }
        }

        private async void QuickFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton btn && btn.Tag is string folderName)
            {
                await App.Current.UserRepository.AddCustomFolderAsync(folderName);
                await LoadCorpusManagementDataAsync();
                if (MainPage.Current != null)
                {
                    await MainPage.Current.ViewModel.LoadBooksAsync(true);
                }
            }
        }

        private async Task RefreshFolderBooksListAsync()
        {
            if (string.IsNullOrEmpty(_currentlySelectedFolderToOrganize)) return;

            var customOrders = await App.Current.UserRepository.GetBookDisplayOrderAsync();

            var folderBooks = _allAssignmentItems
                .Where(b => b.EffectiveFolder.Equals(_currentlySelectedFolderToOrganize, StringComparison.OrdinalIgnoreCase))
                .ToList();

            folderBooks.Sort((a, b) =>
            {
                bool hasA = customOrders.TryGetValue(a.BookKey, out int oa);
                bool hasB = customOrders.TryGetValue(b.BookKey, out int ob);
                if (hasA && hasB && oa != ob) return oa.CompareTo(ob);
                if (hasA && !hasB) return -1;
                string normA = VedaBaseModern.UI.ViewModels.MainViewModel.NormalizeForSorting(a.Title);
                string normB = VedaBaseModern.UI.ViewModels.MainViewModel.NormalizeForSorting(b.Title);
                int cmp = string.Compare(normA, normB, StringComparison.CurrentCultureIgnoreCase);
                return cmp != 0 ? cmp : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            });

            FolderBooksListView.ItemsSource = folderBooks;
            NoBooksInFolderText.Visibility = folderBooks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SelectedFolderToOrganizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedFolderToOrganizeComboBox.SelectedItem is string folderName)
            {
                _currentlySelectedFolderToOrganize = folderName;
                await RefreshFolderBooksListAsync();
            }
        }

        private async void MoveFolderUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string folderName)
            {
                int idx = _folderDisplayItems.FindIndex(f => f.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                if (idx > 0)
                {
                    var item = _folderDisplayItems[idx];
                    _folderDisplayItems.RemoveAt(idx);
                    _folderDisplayItems.Insert(idx - 1, item);
                    await App.Current.UserRepository.SetFolderDisplayOrderAsync(_folderDisplayItems.Select(f => f.Name).ToList());
                    if (MainPage.Current != null)
                    {
                        await MainPage.Current.ViewModel.LoadBooksAsync(true);
                    }
                    FoldersListView.ItemsSource = null;
                    FoldersListView.ItemsSource = _folderDisplayItems;

                    var currentSel = _currentlySelectedFolderToOrganize;
                    SelectedFolderToOrganizeComboBox.ItemsSource = _folderDisplayItems.Select(f => f.Name).ToList();
                    SelectedFolderToOrganizeComboBox.SelectedItem = currentSel;
                }
            }
        }

        private async void MoveFolderDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string folderName)
            {
                int idx = _folderDisplayItems.FindIndex(f => f.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0 && idx < _folderDisplayItems.Count - 1)
                {
                    var item = _folderDisplayItems[idx];
                    _folderDisplayItems.RemoveAt(idx);
                    _folderDisplayItems.Insert(idx + 1, item);
                    await App.Current.UserRepository.SetFolderDisplayOrderAsync(_folderDisplayItems.Select(f => f.Name).ToList());
                    if (MainPage.Current != null)
                    {
                        await MainPage.Current.ViewModel.LoadBooksAsync(true);
                    }
                    FoldersListView.ItemsSource = null;
                    FoldersListView.ItemsSource = _folderDisplayItems;

                    var currentSel = _currentlySelectedFolderToOrganize;
                    SelectedFolderToOrganizeComboBox.ItemsSource = _folderDisplayItems.Select(f => f.Name).ToList();
                    SelectedFolderToOrganizeComboBox.SelectedItem = currentSel;
                }
            }
        }

        private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string folderName)
            {
                await App.Current.UserRepository.DeleteCustomFolderAsync(folderName);
                var order = await App.Current.UserRepository.GetFolderDisplayOrderAsync();
                order.RemoveAll(f => f.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                await App.Current.UserRepository.SetFolderDisplayOrderAsync(order);
                await LoadCorpusManagementDataAsync();
                if (MainPage.Current != null)
                {
                    await MainPage.Current.ViewModel.LoadBooksAsync(true);
                }
            }
        }

        private async void MoveFolderBookUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string bookKey && FolderBooksListView.ItemsSource is List<BookFolderAssignmentItem> items)
            {
                int idx = items.FindIndex(i => i.BookKey == bookKey);
                if (idx > 0)
                {
                    var item = items[idx];
                    items.RemoveAt(idx);
                    items.Insert(idx - 1, item);

                    var orderMap = new Dictionary<string, int>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        orderMap[items[i].BookKey] = (i + 1) * 10;
                    }
                    await App.Current.UserRepository.SetBooksDisplayOrderAsync(orderMap);

                    if (MainPage.Current != null)
                    {
                        await MainPage.Current.ViewModel.LoadBooksAsync(true);
                    }
                    FolderBooksListView.ItemsSource = null;
                    FolderBooksListView.ItemsSource = items;
                }
            }
        }

        private async void MoveFolderBookDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string bookKey && FolderBooksListView.ItemsSource is List<BookFolderAssignmentItem> items)
            {
                int idx = items.FindIndex(i => i.BookKey == bookKey);
                if (idx >= 0 && idx < items.Count - 1)
                {
                    var item = items[idx];
                    items.RemoveAt(idx);
                    items.Insert(idx + 1, item);

                    var orderMap = new Dictionary<string, int>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        orderMap[items[i].BookKey] = (i + 1) * 10;
                    }
                    await App.Current.UserRepository.SetBooksDisplayOrderAsync(orderMap);

                    if (MainPage.Current != null)
                    {
                        await MainPage.Current.ViewModel.LoadBooksAsync(true);
                    }
                    FolderBooksListView.ItemsSource = null;
                    FolderBooksListView.ItemsSource = items;
                }
            }
        }

        private async void SortFolderAlphabetical_Click(object sender, RoutedEventArgs e)
        {
            if (FolderBooksListView.ItemsSource is List<BookFolderAssignmentItem> items && items.Count > 0)
            {
                await App.Current.UserRepository.ResetBookDisplayOrderForKeysAsync(items.Select(i => i.BookKey));
                if (MainPage.Current != null)
                {
                    await MainPage.Current.ViewModel.LoadBooksAsync(true);
                }
                await RefreshFolderBooksListAsync();
            }
        }

        private async void FolderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCategoryAssignmentUpdate) return;
            if (sender is ComboBox cb && cb.Tag is string bookKey && cb.SelectedItem is string selectedFolder)
            {
                if (selectedFolder == "(Default Folder)")
                {
                    await App.Current.UserRepository.RemoveBookCategoryOverrideAsync(bookKey);
                }
                else
                {
                    await App.Current.UserRepository.SetBookCategoryOverrideAsync(bookKey, selectedFolder);
                }
                (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
                if (MainPage.Current != null)
                {
                    await MainPage.Current.ViewModel.LoadBooksAsync(true);
                }
                await LoadCorpusManagementDataAsync();
            }
        }

        private async void ImportBookSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".json");

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                BookImportStatusText.Text = $"Importing '{file.Name}'...";
                BookImportStatusText.Visibility = Visibility.Visible;

                var result = await App.Current.BookImportService.ImportBookFromFileAsync(file.Path);
                BookImportStatusText.Text = result.Message;

                if (result.Success)
                {
                    (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
                    if (MainPage.Current != null)
                    {
                        await MainPage.Current.ViewModel.LoadBooksAsync(true);
                    }
                    await LoadCorpusManagementDataAsync();
                }
            }
            catch (Exception ex)
            {
                BookImportStatusText.Text = $"Import error: {ex.Message}";
                BookImportStatusText.Visibility = Visibility.Visible;
            }
        }

        private async void HighlightColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (sender?.Tag is HighlightColorItemViewModel item)
            {
                await ViewModel.OnColourPickerChangedAsync(item, args.NewColor);
            }
        }

        private async void HighlightHexTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.Tag is HighlightColorItemViewModel item)
            {
                await ViewModel.OnColourHexChangedAsync(item, tb.Text);
            }
        }

        private async void HighlightHexTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (sender is TextBox tb && tb.Tag is HighlightColorItemViewModel item)
                {
                    await ViewModel.OnColourHexChangedAsync(item, tb.Text);
                }
            }
        }

        private async void AddHighlightColour_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.AddHighlightColourCommand.ExecuteAsync(null);
        }

        private async void RemoveHighlightColour_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HighlightColorItemViewModel item)
            {
                await ViewModel.RemoveHighlightColourCommand.ExecuteAsync(item);
            }
        }

        private async void ResetHighlightPalette_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ResetHighlightPaletteCommand.ExecuteAsync(null);
        }

        private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.CheckForUpdatesAsync();
        }

        private async void ApplyPatchUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ApplyPatchUpdateAsync();
        }

        private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            string url = !string.IsNullOrWhiteSpace(ViewModel.UpdateDownloadUrl)
                ? ViewModel.UpdateDownloadUrl
                : "https://github.com/itzabhinav01/prabhupada-connect/releases";
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] Failed to launch update URL: {ex.Message}");
            }
        }

        public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BoolToInvertedVis(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
        public bool BoolToInverted(bool b) => !b;
        public Visibility StringToVis(string? s) => string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;
    }
}
