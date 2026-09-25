using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class NotesPage : Page
    {
        public NotesViewModel ViewModel { get; }

        public NotesPage()
        {
            this.InitializeComponent();
            ViewModel = new NotesViewModel(App.Current.Repository, App.Current.UserRepository);
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadNotesAsync();
            if (e.Parameter is string tag && !string.IsNullOrWhiteSpace(tag))
            {
                ViewModel.SelectedTag = tag.StartsWith("#") ? tag : $"#{tag}";
            }
        }

        // ContentDialog has no declarative "IsOpen" binding - it is shown/
        // hidden imperatively, so the dialog's own lifecycle is driven from
        // the ViewModel's IsEditorOpen flag here rather than duplicating
        // open/close logic in every command handler.
        private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(NotesViewModel.IsEditorOpen)) return;

            if (ViewModel.IsEditorOpen)
            {
                VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(NoteEditorDialog, this.XamlRoot);
                await NoteEditorDialog.ShowAsync();
            }
            else
            {
                NoteEditorDialog.Hide();
            }
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is NoteListItem item)
            {
                ViewModel.SelectedNote = item;
            }
        }

        private void TagChipButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content != null)
            {
                ViewModel.SelectedTag = btn.Content.ToString() ?? "All";
            }
        }

        private void EditSelectedNote_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedNote != null)
            {
                ViewModel.OpenNoteForEditCommand.Execute(ViewModel.SelectedNote);
            }
        }

        private async void DeleteSelectedNote_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedNote != null)
            {
                var dialog = new ContentDialog
                {
                    Title = "Delete Realization",
                    Content = $"Are you sure you want to delete \"{ViewModel.SelectedNote.TitleDisplay}\"?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);
                var res = await dialog.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    await App.Current.UserRepository.DeleteNoteAsync(ViewModel.SelectedNote.Id);
                    await ViewModel.LoadNotesAsync();
                }
            }
        }

        private void OpenScriptureInNewTab_Click(object sender, RoutedEventArgs e)
        {
            var note = ViewModel.SelectedNote;
            if (note != null && !string.IsNullOrEmpty(note.RecordKey))
            {
                if (MainPage.Current != null)
                {
                    MainPage.Current.CreateNewTab(note.Reference, "\uE8A5", typeof(ReadingPage), note.RecordKey);
                }
                else
                {
                    this.Frame.Navigate(typeof(ReadingPage), note.RecordKey);
                }
            }
        }

        private async void ExportObsidianButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("Obsidian Vault Archive (*.zip)", new List<string> { ".zip" });
                picker.SuggestedFileName = $"VedaBase_Obsidian_Vault_{DateTime.Now:yyyyMMdd_HHmmss}";

                var hwnd = App.Current.MainWindowInstance?.WindowHandle ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await ViewModel.ExportToObsidianVaultAsync(file.Path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotesPage] Obsidian export failed: {ex}");
            }
        }

        private async void ExportPrintHtmlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string html = ViewModel.GeneratePrintableHtml(ViewModel.SelectedNote);
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("HTML Document (Print to PDF) (*.html)", new List<string> { ".html" });
                picker.SuggestedFileName = $"VedaBase_Notes_Print_{DateTime.Now:yyyyMMdd_HHmmss}";

                var hwnd = App.Current.MainWindowInstance?.WindowHandle ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await Windows.Storage.FileIO.WriteTextAsync(file, html);
                    if (MainPage.Current != null)
                    {
                        MainPage.Current.CreateNewTab("Print Notes", "\uE749", typeof(WebBrowserPage), file.Path);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotesPage] Print HTML export failed: {ex}");
            }
        }

        private async void ExportAllNotesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var notes = ViewModel.Notes;
                if (notes == null || notes.Count == 0) return;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# VedaBase Research Notebook");
                sb.AppendLine($"*Exported on {DateTime.Now:yyyy-MM-dd HH:mm} — Total Realizations: {notes.Count}*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();

                // Group by Scripture / Reference
                var grouped = notes.GroupBy(n => string.IsNullOrEmpty(n.Reference) ? "General Research Notes" : $"{n.BookTitle} — {n.Reference}");
                foreach (var group in grouped)
                {
                    sb.AppendLine($"## {group.Key}");
                    sb.AppendLine();
                    foreach (var note in group)
                    {
                        if (!string.IsNullOrWhiteSpace(note.Title))
                        {
                            sb.AppendLine($"### {note.Title}");
                        }
                        sb.AppendLine($"*Date: {note.UpdatedDisplay}*");
                        sb.AppendLine();

                        // Clean HTML tags into clean Markdown
                        string cleanContent = System.Text.RegularExpressions.Regex.Replace(note.Content, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        cleanContent = System.Text.RegularExpressions.Regex.Replace(cleanContent, @"</p>", "\n\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        cleanContent = System.Text.RegularExpressions.Regex.Replace(cleanContent, @"<[^>]+>", "");

                        sb.AppendLine(cleanContent.Trim());
                        sb.AppendLine();
                        sb.AppendLine("---");
                        sb.AppendLine();
                    }
                }

                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("Markdown Notebook (*.md)", new List<string> { ".md" });
                picker.FileTypeChoices.Add("Plain Text (*.txt)", new List<string> { ".txt" });
                picker.SuggestedFileName = $"VedaBase_Notebook_{DateTime.Now:yyyyMMdd_HHmmss}";

                var hwnd = App.Current.MainWindowInstance?.WindowHandle ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await Windows.Storage.FileIO.WriteTextAsync(file, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotesPage] Export failed: {ex}");
            }
        }

        private async void NoteEditorDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            await ViewModel.SaveNoteCommand.ExecuteAsync(null);
            if (string.IsNullOrEmpty(ViewModel.EditorErrorMessage) && !ViewModel.IsEditorOpen)
            {
                args.Cancel = false;
            }
        }

        private async void NoteEditorDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            await ViewModel.DeleteEditingNoteCommand.ExecuteAsync(null);
        }

        private void OpenInReadingView_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ViewModel.EditRecordKey)) return;
            ViewModel.CloseEditorCommand.Execute(null);

            if (!string.IsNullOrEmpty(ViewModel.EditField) && ViewModel.EditStartOffset >= 0 && ViewModel.EditLength > 0)
            {
                this.Frame.Navigate(typeof(ReadingPage),
                    new HighlightNavigationTarget(ViewModel.EditRecordKey, ViewModel.EditField!, ViewModel.EditStartOffset, ViewModel.EditLength));
            }
            else
            {
                this.Frame.Navigate(typeof(ReadingPage), ViewModel.EditRecordKey);
            }
        }

        public Visibility BoolToVis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        public Visibility Not(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
        public Visibility HasText(string s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CountToVis(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowEmptyState(bool isLoading, int count) => (!isLoading && count == 0) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowFilterEmptyState(bool isFilterEmpty, int totalCount) => (isFilterEmpty && totalCount > 0) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowFilteredResults(bool isFilterEmpty, int totalCount) => (!isFilterEmpty && totalCount > 0) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NullToVis(object? o) => o == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NotNullToVis(object? o) => o != null ? Visibility.Visible : Visibility.Collapsed;
        public string EditorTitle(bool isEditingExisting) => isEditingExisting ? "Edit Note" : "New Note";
    }
}
