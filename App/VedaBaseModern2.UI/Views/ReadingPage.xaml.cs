using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using VedaBaseModern.Core.Models;
using VedaBaseModern.UI.ViewModels;
using VedaBaseModern.UI.Services;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class ReadingPage : Page
    {
        public ReadingViewModel ViewModel { get; }

        private bool _isWebReady;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _findDebounceTimer;
        private int _findTotalMatches;
        private int _findActiveMatchIndex = -1;
        private List<JsonElement> _findMatchList = new();
        private string? _pendingSearchQuery;

        public ReadingPage()
        {
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            this.InitializeComponent();

            ViewModel = new ReadingViewModel(App.Current.Repository, App.Current.UserRepository, App.Current.SettingsService);
            ViewModel.ContentReady += ViewModel_ContentReady;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            Loaded += ReadingPage_Loaded;

            this.ActualThemeChanged += (s, e) => { SyncThemeToWeb(); SyncParallelTheme(); };
            App.Current.ThemeChanged += (theme) => { SyncThemeToWeb(theme); SyncParallelTheme(); };
            if (App.Current.ReadingPreferencesService != null)
            {
                App.Current.ReadingPreferencesService.TextBrightnessChanged += (brightness) => SyncBrightnessToWeb();
            }
        }

        private static void LogDebug(string msg)
        {
            try { File.AppendAllText(@"C:\VedaBaseModern2\reader_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n"); } catch { }
        }

        private static CoreWebView2Environment? _sharedEnvironment;
        private static Task<CoreWebView2Environment>? _sharedEnvTask;
        private static readonly SemaphoreSlim _envLock = new(1, 1);
        private Task? _initWebViewTask;

        public static Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
        {
            // Double-check pattern: return existing task if already in-flight or done successfully
            if (_sharedEnvTask != null && !_sharedEnvTask.IsFaulted && !_sharedEnvTask.IsCanceled)
                return _sharedEnvTask;

            // If the previous attempt faulted, reset and retry
            _sharedEnvTask = CreateEnvironmentAsync();
            return _sharedEnvTask;
        }

        private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            await _envLock.WaitAsync();
            try
            {
                // Re-check inside the lock - once created, NEVER discard!
                if (_sharedEnvironment != null)
                    return _sharedEnvironment;

                string baseFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VedaBaseModern",
                    "WebView2");
                Directory.CreateDirectory(baseFolder);

                // Clean up stale session folders from previous exited processes
                try
                {
                    var dInfo = new DirectoryInfo(baseFolder);
                    if (dInfo.Exists)
                    {
                        foreach (var dir in dInfo.GetDirectories("Session_*"))
                        {
                            if (int.TryParse(dir.Name.Substring("Session_".Length), out int pid) && pid != Environment.ProcessId)
                            {
                                try
                                {
                                    var proc = System.Diagnostics.Process.GetProcessById(pid);
                                }
                                catch (ArgumentException)
                                {
                                    try { dir.Delete(true); } catch { }
                                }
                            }
                        }
                    }
                }
                catch { }

                string userDataFolder = Path.Combine(baseFolder, $"Session_{Environment.ProcessId}");
                Directory.CreateDirectory(userDataFolder);

                LogDebug($"Creating CoreWebView2Environment with userDataFolder={userDataFolder}");
                _sharedEnvironment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions());
                LogDebug("CoreWebView2Environment created successfully");
                return _sharedEnvironment;
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to create CoreWebView2Environment: {ex.HResult:X8} {ex.Message}");
                throw;
            }
            finally
            {
                _envLock.Release();
            }
        }

        private async void ReadingPage_Loaded(object sender, RoutedEventArgs e)
        {
            LogDebug("ReadingPage_Loaded");
            if (ViewModel != null)
            {
                ViewModel.ContentReady -= ViewModel_ContentReady;
                ViewModel.ContentReady += ViewModel_ContentReady;
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            await EnsureWebViewInitializedAsync();
            SyncContentToWeb();
        }

        private void ReadingPage_Unloaded(object sender, RoutedEventArgs e)
        {
            LogDebug("ReadingPage_Unloaded");
            // Do not permanently unsubscribe since page is cached (NavigationCacheMode.Required)
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LogDebug($"OnNavigatedTo parameter={e.Parameter}");

            if (ViewModel != null)
            {
                ViewModel.ContentReady -= ViewModel_ContentReady;
                ViewModel.ContentReady += ViewModel_ContentReady;
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            string? targetKey = e.Parameter switch
            {
                string key => key,
                HighlightNavigationTarget hl => hl.RecordKey,
                SearchResultNavigationTarget sr => (_pendingSearchQuery = sr.SearchQuery) != null ? sr.RecordKey : sr.RecordKey,
                _ => null
            };

            await EnsureWebViewInitializedAsync();

            if (ViewModel != null)
            {
                if (!string.IsNullOrEmpty(targetKey))
                {
                    await ViewModel.LoadRecordAsync(targetKey);
                }
                else if (string.IsNullOrEmpty(ViewModel.CurrentRecord?.RecordKey))
                {
                    // Default to first verse of Bhagavad-gītā
                    await ViewModel.LoadRecordAsync("BG-1-1");
                }
            }

            SyncContentToWeb();
        }

        private async Task EnsureWebViewInitializedAsync()
        {
            if (!this.IsLoaded)
            {
                var tcs = new TaskCompletionSource<bool>();
                RoutedEventHandler? handler = null;
                handler = (s, e) =>
                {
                    this.Loaded -= handler;
                    tcs.TrySetResult(true);
                };
                this.Loaded += handler;
                await tcs.Task;
            }

            // Always retry if task completed without success (_isWebReady still false)
            if (_initWebViewTask == null || _initWebViewTask.IsFaulted ||
                (_initWebViewTask.IsCompleted && !_isWebReady && ReaderWebView?.CoreWebView2 == null))
            {
                _initWebViewTask = InitializeWebViewInternalAsync();
            }
            await _initWebViewTask;
        }

        private async Task InitializeWebViewInternalAsync()
        {
            if (ReaderWebView == null)
            {
                LogDebug("ReaderWebView is null");
                return;
            }

            if (_isWebReady && ReaderWebView.CoreWebView2 != null)
            {
                LogDebug($"InitializeWebView skipped: already ready");
                return;
            }

            const int maxRetries = 4;
            int delayMs = 300;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    string assetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Reader");
                    LogDebug($"InitializeWebView attempt {attempt}/{maxRetries}: assetsFolder exists={Directory.Exists(assetsFolder)}, htmlExists={File.Exists(Path.Combine(assetsFolder, "reader.html"))}");

                    ReaderWebView.CoreWebView2Initialized -= ReaderWebView_CoreWebView2Initialized;
                    ReaderWebView.CoreWebView2Initialized += ReaderWebView_CoreWebView2Initialized;

                    var env = await GetSharedEnvironmentAsync();
                    LogDebug($"Awaiting EnsureCoreWebView2Async (attempt {attempt})...");
                    await ReaderWebView.EnsureCoreWebView2Async(env);
                    var coreWebView2 = ReaderWebView.CoreWebView2;
                    LogDebug($"EnsureCoreWebView2Async completed, CoreWebView2!=null={coreWebView2 != null}");

                    if (coreWebView2 == null)
                    {
                        LogDebug($"ERROR: CoreWebView2 is null after EnsureCoreWebView2Async (attempt {attempt})!");
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(delayMs);
                            delayMs *= 2;
                            continue;
                        }
                        return;
                    }

                    coreWebView2.SetVirtualHostNameToFolderMapping(
                        "reader.local",
                        assetsFolder,
                        CoreWebView2HostResourceAccessKind.Allow);

                    coreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    coreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                    coreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    coreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                    coreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    coreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                    LogDebug("Navigating to https://reader.local/reader.html");
                    coreWebView2.Navigate("https://reader.local/reader.html");
                    return; // Success!
                }
                catch (System.Runtime.InteropServices.COMException comEx) when (
                    (uint)comEx.HResult == 0x800700AA || // ERROR_BUSY
                    (uint)comEx.HResult == 0x80004005)   // E_FAIL
                {
                    LogDebug($"WebView2 COMException on attempt {attempt}: HResult=0x{comEx.HResult:X8} {comEx.Message}");
                    if (attempt < maxRetries)
                    {
                        LogDebug($"Retrying in {delayMs}ms...");
                        await Task.Delay(delayMs);
                        delayMs *= 2;
                    }
                    else
                    {
                        LogDebug($"WebView2 init failed after {maxRetries} attempts");
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"WebView2 Init Error: {ex.GetType().Name}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[ReadingPage] WebView2 Init Error: {ex}");
                    return;
                }
            }
        }

        private void ReaderWebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
        {
            if (args.Exception != null)
            {
                LogDebug($"ReaderWebView CoreWebView2Initialized Exception: {args.Exception}");
            }
            else
            {
                LogDebug("ReaderWebView CoreWebView2Initialized successfully");
            }
        }

        private void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            LogDebug($"NavigationCompleted: success={e.IsSuccess}, errorStatus={e.WebErrorStatus}");
            if (e.IsSuccess)
            {
                _isWebReady = true;
                SyncThemeToWeb();
                SyncBrightnessToWeb();
                SyncContentToWeb();
            }
        }

        private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            if (!string.IsNullOrWhiteSpace(args.Uri))
            {
                string tabTitle = "Web";
                try { tabTitle = new Uri(args.Uri).Host; } catch { }
                if (MainPage.Current != null)
                {
                    MainPage.Current.CreateNewTab(tabTitle, "\uE774", typeof(WebBrowserPage), args.Uri);
                }
            }
        }

        private async void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                LogDebug($"CoreWebView2_WebMessageReceived: {args.WebMessageAsJson}");
                using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                string action = doc.RootElement.GetProperty("action").GetString() ?? "";

                switch (action)
                {
                    case "js_error":
                        string jsErr = doc.RootElement.GetProperty("error").GetString() ?? "";
                        LogDebug($"[JS Error] {jsErr}");
                        break;

                    case "ready":
                        _isWebReady = true;
                        SyncThemeToWeb();
                        SyncBrightnessToWeb();
                        SyncContentToWeb();
                        break;

                    case "open_web_tab":
                        string webUrl = doc.RootElement.GetProperty("url").GetString() ?? "";
                        string webTitle = doc.RootElement.TryGetProperty("title", out var titleEl2) ? (titleEl2.GetString() ?? "") : "";
                        if (!string.IsNullOrWhiteSpace(webUrl))
                        {
                            if (string.IsNullOrWhiteSpace(webTitle))
                            {
                                try { webTitle = new Uri(webUrl).Host; } catch { webTitle = "Web"; }
                            }
                            if (MainPage.Current != null)
                            {
                                MainPage.Current.CreateNewTab(webTitle, "\uE774", typeof(WebBrowserPage), webUrl);
                            }
                        }
                        break;

                    case "focus_verse":
                        string key = doc.RootElement.GetProperty("recordKey").GetString() ?? "";
                        if (!string.IsNullOrEmpty(key))
                        {
                            await ViewModel.LoadRecordAsync(key);
                            ViewModel.IsContinuousChapter = false;
                            UpdateViewModeButton();
                        }
                        break;

                    case "navigate_reference":
                        string targetRef = doc.RootElement.GetProperty("reference").GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(targetRef))
                        {
                            string? recordKey = await App.Current.ReferenceService.TryResolveExactAsync(targetRef);
                            if (recordKey != null)
                            {
                                if (MainPage.Current != null)
                                {
                                    MainPage.Current.CreateNewTab(targetRef, "\uE8A5", typeof(ReadingPage), recordKey);
                                }
                                else
                                {
                                    await ViewModel.LoadRecordAsync(recordKey);
                                    ViewModel.IsContinuousChapter = false;
                                    UpdateViewModeButton();
                                }
                            }
                            else
                            {
                                // External scripture citation (e.g. from an SPS verse or outside quotation):
                                // open a search tab so the reader can explore where Śrīla Prabhupāda explains or quotes it.
                                if (MainPage.Current != null)
                                {
                                    MainPage.Current.CreateNewTab($"Search: {targetRef}", "\uE721", typeof(SearchPage), targetRef);
                                }
                            }
                        }
                        break;

                    case "save_note":
                        string noteId = doc.RootElement.TryGetProperty("noteId", out var nidEl) ? (nidEl.GetString() ?? "") : "";
                        string rk = doc.RootElement.GetProperty("recordKey").GetString() ?? "";
                        string content = doc.RootElement.GetProperty("content").GetString() ?? "";
                        string title = doc.RootElement.TryGetProperty("title", out var titleEl) ? (titleEl.GetString() ?? "") : "";
                        var savedNote = await ViewModel.SaveOrUpdateNoteAsync(string.IsNullOrEmpty(noteId) ? null : noteId, rk, content, string.IsNullOrEmpty(title) ? null : title);
                        if (savedNote != null)
                        {
                            string savedJson = JsonSerializer.Serialize(savedNote);
                            await ReaderWebView.ExecuteScriptAsync($"reader.onNoteSaved(JSON.parse({JsonSerializer.Serialize(savedJson)}));");
                        }
                        break;

                    case "delete_note":
                        string delId = doc.RootElement.GetProperty("noteId").GetString() ?? "";
                        string delRk = doc.RootElement.GetProperty("recordKey").GetString() ?? "";
                        bool delSuccess = await ViewModel.DeleteNoteByIdAsync(delId);
                        if (delSuccess)
                        {
                            await ReaderWebView.ExecuteScriptAsync($"reader.onNoteDeleted('{delId}', '{delRk}');");
                        }
                        break;

                    case "export_note":
                        string expContent = doc.RootElement.GetProperty("content").GetString() ?? "";
                        string expRef = doc.RootElement.GetProperty("reference").GetString() ?? "Note";
                        await ExportNoteToFileAsync(expRef, expContent);
                        break;

                    case "add_highlight":
                        await HandleAddHighlightMessage(doc.RootElement);
                        break;

                    case "remove_highlight":
                        string hlId = doc.RootElement.GetProperty("highlightId").GetString() ?? "";
                        if (!string.IsNullOrEmpty(hlId))
                        {
                            await App.Current.UserRepository.RemoveHighlightAsync(hlId);
                        }
                        break;

                    case "shortcut_find":
                        OpenFindBar();
                        break;

                    case "shortcut_theme":
                        await App.Current.ToggleThemeAsync();
                        break;

                    case "shortcut_prev":
                        if (ViewModel.GoPreviousCommand.CanExecute(null)) ViewModel.GoPreviousCommand.Execute(null);
                        break;

                    case "shortcut_next":
                        if (ViewModel.GoNextCommand.CanExecute(null)) ViewModel.GoNextCommand.Execute(null);
                        break;

                    case "shortcut_escape":
                        CloseFindBar();
                        if (_isZenMode) ExitZenMode();
                        break;

                    case "shortcut_zoom_in":
                        ViewModel.ZoomIn();
                        SyncFontSizesToWeb();
                        break;

                    case "shortcut_zoom_out":
                        ViewModel.ZoomOut();
                        SyncFontSizesToWeb();
                        break;

                    case "shortcut_zoom_reset":
                        ViewModel.ResetZoom();
                        SyncFontSizesToWeb();
                        break;

                    case "exit_zen_mode":
                        ExitZenMode();
                        break;

                    case "filter_hashtag":
                        string htag = doc.RootElement.GetProperty("tag").GetString() ?? "";
                        if (!string.IsNullOrEmpty(htag))
                        {
                            if (MainPage.Current != null)
                            {
                                MainPage.Current.CreateNewTab($"Notes: #{htag.TrimStart('#')}", "\uE70B", typeof(NotesPage), htag);
                            }
                        }
                        break;

                    case "concordance_lookup":
                        string word = doc.RootElement.GetProperty("word").GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(word))
                        {
                            await OpenConcordanceDialogAsync(word);
                        }
                        break;

                    case "copy_text":
                        // Text copied in web engine
                        break;

                    case "add_note":
                        // Note initiated
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadingPage] Message Error: {ex}");
            }
        }

        private async Task HandleAddHighlightMessage(JsonElement el)
        {
            try
            {
                string recordKey = el.GetProperty("recordKey").GetString() ?? "";
                string field = el.GetProperty("field").GetString() ?? "";
                string text = el.GetProperty("text").GetString() ?? "";
                string colorStr = el.GetProperty("color").GetString() ?? "Yellow";

                if (Enum.TryParse<HighlightColor>(colorStr, true, out var color))
                {
                    var created = await App.Current.UserRepository.AddHighlightAsync(recordKey, field, 0, text.Length, text, color);
                    if (created != null && !string.IsNullOrEmpty(created.Id) && _isWebReady && ReaderWebView?.CoreWebView2 != null)
                    {
                        string escapedText = JsonSerializer.Serialize(text);
                        await ReaderWebView.ExecuteScriptAsync($"reader.attachHighlightId('{created.Id}', {escapedText});");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadingPage] Highlight save error: {ex}");
            }
        }

        private async Task OpenConcordanceDialogAsync(string word)
        {
            try
            {
                var dialog = new ConcordanceDialog
                {
                    XamlRoot = this.XamlRoot
                };
                VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);
                dialog.VerseSelected += async (key) =>
                {
                    await ViewModel.LoadRecordAsync(key);
                };
                _ = dialog.SearchAsync(word);
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadingPage] ConcordanceDialog error: {ex}");
            }
        }

        private void ViewModel_ContentReady()
        {
            SyncContentToWeb();
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.IsContinuousChapter))
            {
                UpdateViewModeButton();
                SyncContentToWeb();
            }
            else if (e.PropertyName == nameof(ViewModel.CurrentRecord) ||
                     e.PropertyName == nameof(ViewModel.ChapterRecords) ||
                     e.PropertyName == nameof(ViewModel.ShowTransliteration) ||
                     e.PropertyName == nameof(ViewModel.ShowSynonyms) ||
                     e.PropertyName == nameof(ViewModel.ShowPurport) ||
                     e.PropertyName == nameof(ViewModel.ShowPronunciationGuide))
            {
                SyncContentToWeb();
            }
        }

        private void UpdateViewModeButton()
        {
            if (ViewModeText != null && ViewModeIcon != null)
            {
                if (ViewModel.IsContinuousChapter)
                {
                    ViewModeText.Text = "Verse View";
                    ViewModeIcon.Glyph = "\uE8A5";
                }
                else
                {
                    ViewModeText.Text = "Continuous View";
                    ViewModeIcon.Glyph = "\uE8A5";
                }
            }
        }

        private async void SyncContentToWeb()
        {
            LogDebug($"SyncContentToWeb called: _isWebReady={_isWebReady}, CoreWebView2!=null={ReaderWebView?.CoreWebView2 != null}, CurrentRecord={ViewModel.CurrentRecord?.RecordKey}, IsContinuous={ViewModel.IsContinuousChapter}");
            if (!_isWebReady || ReaderWebView?.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                var options = new
                {
                    showTransliteration = ViewModel.ShowTransliteration,
                    showSynonyms = ViewModel.ShowSynonyms,
                    showPurport = ViewModel.ShowPurport,
                    showPronunciationGuide = ViewModel.ShowPronunciationGuide
                };
                string optionsJson = JsonSerializer.Serialize(options);

                if (ViewModel.IsContinuousChapter && ViewModel.ChapterRecords != null && ViewModel.ChapterRecords.Count > 0)
                {
                    string recordsJson = JsonSerializer.Serialize(ViewModel.ChapterRecords);
                    string titleJson = JsonSerializer.Serialize(ViewModel.ChapterHeader ?? "Bhagavad-gītā");
                    string subtitleJson = JsonSerializer.Serialize(ViewModel.ChapterSubtitle ?? "Continuous Chapter Reading View");

                    var keys = ViewModel.ChapterRecords.Select(r => r.RecordKey).ToList();
                    var highlights = await ViewModel.GetHighlightsForChapterAsync(keys);
                    string hlJson = JsonSerializer.Serialize(highlights);
                    var notes = await ViewModel.GetNotesForChapterAsync(keys);
                    string notesJson = JsonSerializer.Serialize(notes);

                    LogDebug($"Executing renderChapter for {ViewModel.ChapterRecords.Count} records");
                    string chapterScript = $@"
                        try {{
                            reader.renderChapter({titleJson}, {subtitleJson}, JSON.parse({JsonSerializer.Serialize(recordsJson)}), JSON.parse({JsonSerializer.Serialize(optionsJson)}), JSON.parse({JsonSerializer.Serialize(hlJson)}), JSON.parse({JsonSerializer.Serialize(notesJson)}));
                            'OK';
                        }} catch (e) {{
                            'RENDER_ERROR: ' + e.message + ' at ' + e.stack;
                        }}";
                    string res = await ReaderWebView.ExecuteScriptAsync(chapterScript);
                    LogDebug($"renderChapter executed, result={res}");

                    // Scroll to active verse inside continuous chapter
                    if (ViewModel.CurrentRecord != null && !string.IsNullOrEmpty(ViewModel.CurrentRecord.RecordKey))
                    {
                        string refKey = ViewModel.CurrentRecord.RecordKey;
                        await ReaderWebView.ExecuteScriptAsync($"reader.scrollToVerse('{refKey}');");
                    }
                }
                else if (ViewModel.CurrentRecord != null && !string.IsNullOrEmpty(ViewModel.CurrentRecord.RecordKey))
                {
                    string recordJson = JsonSerializer.Serialize(ViewModel.CurrentRecord);
                    var highlights = await ViewModel.GetHighlightsForRecordAsync(ViewModel.CurrentRecord.RecordKey);
                    string hlJson = JsonSerializer.Serialize(highlights);
                    var notes = await ViewModel.GetNotesForRecordAsync(ViewModel.CurrentRecord.RecordKey);
                    string notesJson = JsonSerializer.Serialize(notes);

                    LogDebug($"Executing renderVerse for {ViewModel.CurrentRecord.RecordKey}");
                    string verseScript = $@"
                        try {{
                            reader.renderVerse(JSON.parse({JsonSerializer.Serialize(recordJson)}), JSON.parse({JsonSerializer.Serialize(optionsJson)}), JSON.parse({JsonSerializer.Serialize(hlJson)}), JSON.parse({JsonSerializer.Serialize(notesJson)}));
                            'OK';
                        }} catch (e) {{
                            'RENDER_ERROR: ' + e.message + ' at ' + e.stack;
                        }}";
                    string res = await ReaderWebView.ExecuteScriptAsync(verseScript);
                    LogDebug($"renderVerse executed, result={res}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"SyncContentToWeb Error: {ex}");
                System.Diagnostics.Debug.WriteLine($"[ReadingPage] SyncContentToWeb Error: {ex}");
            }

            if (!string.IsNullOrEmpty(_pendingSearchQuery))
            {
                string q = _pendingSearchQuery;
                _pendingSearchQuery = null;
                _ = ReaderWebView?.ExecuteScriptAsync($"window.reader && window.reader.highlightSearchTerms({JsonSerializer.Serialize(q)});");
            }

            if (_isSplitViewOpen && SideNotebookPanel != null && SideNotebookPanel.Visibility == Visibility.Visible)
            {
                PopulateSideNotebookForCurrentVerse();
            }
        }

        private async Task ExportNoteToFileAsync(string reference, string content)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("Markdown Document (*.md)", new List<string> { ".md" });
                picker.FileTypeChoices.Add("Plain Text (*.txt)", new List<string> { ".txt" });
                string safeRef = reference.Replace(" ", "_").Replace(":", "-").Replace(".", "-");
                picker.SuggestedFileName = $"{safeRef}_Realization_{DateTime.Now:yyyyMMdd}";

                var hwnd = App.Current.MainWindowInstance?.WindowHandle ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await Windows.Storage.FileIO.WriteTextAsync(file, content);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReadingPage] Export note error: {ex}");
            }
        }

        private async void SyncThemeToWeb(AppTheme? explicitTheme = null)
        {
            if (!_isWebReady || ReaderWebView?.CoreWebView2 == null) return;

            bool isDark;
            bool isCustom = false;
            AppTheme themeToUse;

            if (explicitTheme.HasValue)
            {
                themeToUse = explicitTheme.Value;
            }
            else
            {
                var settings = await App.Current.SettingsService.GetSettingsAsync();
                themeToUse = settings.Theme;
            }

            if (themeToUse == AppTheme.Dark)
            {
                isDark = true;
            }
            else if (themeToUse == AppTheme.Light)
            {
                isDark = false;
            }
            else if (themeToUse == AppTheme.Custom)
            {
                isCustom = true;
                isDark = !string.Equals(CustomThemeService.ActiveCustomTheme?.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase);
            }
            else // System
            {
                isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark
                    || this.ActualTheme == ElementTheme.Dark;
            }

            string bgColor;
            string primaryText;
            string secondaryText;
            string accentColor;
            string cardBackground;
            string cardBorder;

            if (isCustom && CustomThemeService.ActiveCustomTheme is { } custom)
            {
                bgColor = custom.PageBackground;
                primaryText = custom.PrimaryText;
                secondaryText = custom.SecondaryText;
                accentColor = custom.AccentColor;
                cardBackground = custom.CardBackground;
                cardBorder = custom.DividerColor;
            }
            else
            {
                bgColor = isDark ? "#0F281E" : "#F3D4A5";
                primaryText = isDark ? "#F2F4F3" : "#111111";
                secondaryText = isDark ? "#A8BDB4" : "#3A3024";
                accentColor = isDark ? "#D4AF37" : "#9B6818";
                cardBackground = isDark ? "#16382A" : "#F7DCAF";
                cardBorder = isDark ? "rgba(255,255,255,0.08)" : "rgba(0,0,0,0.08)";
            }

            var themeObj = new
            {
                backgroundColor = bgColor,
                primaryTextColor = primaryText,
                secondaryTextColor = secondaryText,
                accentColor = accentColor,
                cardBackground = cardBackground,
                cardBorder = cardBorder
            };

            string themeJson = JsonSerializer.Serialize(themeObj);
            await ReaderWebView.ExecuteScriptAsync($"reader.setTheme({themeJson});");
        }

        private async void SyncBrightnessToWeb()
        {
            if (!_isWebReady || ReaderWebView?.CoreWebView2 == null) return;
            var pref = App.Current.ReadingPreferencesService?.ActivePreferences;
            int brightness = pref?.TextBrightness ?? 100;
            await ReaderWebView.ExecuteScriptAsync($"reader.setTextBrightness({brightness});");
        }

        private async void SyncFontSizesToWeb()
        {
            if (!_isWebReady || ReaderWebView?.CoreWebView2 == null || ViewModel == null) return;
            var sizes = new
            {
                verse = Math.Round(ViewModel.DevanagariFontSize),
                transliteration = Math.Round(ViewModel.TransliterationFontSize),
                synonyms = Math.Round(ViewModel.SynonymsFontSize),
                translation = Math.Round(ViewModel.TranslationFontSize),
                purport = Math.Round(ViewModel.PurportFontSize)
            };
            string json = JsonSerializer.Serialize(sizes);
            await ReaderWebView.ExecuteScriptAsync($"reader.setFontSizes({json});");
        }

        // ---- Top Bar Actions ----

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack) this.Frame.GoBack();
        }

        private async void ViewModeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ToggleViewModeAsync();
            UpdateViewModeButton();
            SyncContentToWeb();
        }

        // ---- In-page Find (Ctrl+F) ----

        private void FindButton_Click(object sender, RoutedEventArgs e) => OpenFindBar();
        private void FindAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            OpenFindBar();
            args.Handled = true;
        }

        private void PrevAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ViewModel.GoPreviousCommand.CanExecute(null)) ViewModel.GoPreviousCommand.Execute(null);
            args.Handled = true;
        }

        private void NextAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ViewModel.GoNextCommand.CanExecute(null)) ViewModel.GoNextCommand.Execute(null);
            args.Handled = true;
        }

        private void BookmarkAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ViewModel.ToggleBookmarkCommand.CanExecute(null)) ViewModel.ToggleBookmarkCommand.Execute(null);
            args.Handled = true;
        }

        private void OpenFindBar()
        {
            FindBarCard.Visibility = Visibility.Visible;
            FindQueryBox.Focus(FocusState.Programmatic);
            FindQueryBox.SelectAll();
            ExecuteFind(FindQueryBox.Text);
        }

        private void CloseFindBar()
        {
            FindBarCard.Visibility = Visibility.Collapsed;
            FindQueryBox.Text = string.Empty;
            _findTotalMatches = 0;
            _findActiveMatchIndex = -1;
            _findMatchList.Clear();
            if (_isWebReady && ReaderWebView?.CoreWebView2 != null)
            {
                _ = ReaderWebView.ExecuteScriptAsync("reader.clearFindSearch();");
            }
        }

        private void FindCloseButton_Click(object sender, RoutedEventArgs e) => CloseFindBar();

        private void FindQueryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_findDebounceTimer == null)
            {
                _findDebounceTimer = DispatcherQueue.CreateTimer();
                _findDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
                _findDebounceTimer.IsRepeating = false;
                _findDebounceTimer.Tick += (s, ev) => ExecuteFind(FindQueryBox.Text);
            }
            _findDebounceTimer.Stop();
            _findDebounceTimer.Start();
        }

        private void FindQueryBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                bool isShift = (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                if (isShift) FindPrevious(); else FindNext();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseFindBar();
                e.Handled = true;
            }
        }

        private void FindOption_Toggled(object sender, RoutedEventArgs e)
        {
            ExecuteFind(FindQueryBox.Text);
        }

        private async void ExecuteFind(string query)
        {
            if (!_isWebReady || ReaderWebView?.CoreWebView2 == null) return;

            if (string.IsNullOrWhiteSpace(query))
            {
                await ReaderWebView.ExecuteScriptAsync("reader.clearFindSearch();");
                FindCounterText.Text = "0 of 0";
                _findTotalMatches = 0;
                _findActiveMatchIndex = -1;
                _findMatchList.Clear();
                return;
            }

            string escaped = JsonSerializer.Serialize(query);
            bool matchCase = FindMatchCaseToggle?.IsChecked == true;
            bool matchWord = FindMatchWordToggle?.IsChecked == true;
            string optionsJson = JsonSerializer.Serialize(new { matchCase, matchWord });
            string resultJson = await ReaderWebView.ExecuteScriptAsync($"reader.findSearch({escaped}, {optionsJson});");

            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                var root = doc.RootElement;
                _findTotalMatches = root.GetProperty("totalMatches").GetInt32();
                _findActiveMatchIndex = root.GetProperty("activeIndex").GetInt32();

                _findMatchList.Clear();
                if (root.TryGetProperty("matches", out var matchesArr) && matchesArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in matchesArr.EnumerateArray())
                    {
                        _findMatchList.Add(m.Clone());
                    }
                }

                UpdateFindCounterUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Find Error] {ex}");
            }
        }

        private void UpdateFindCounterUI()
        {
            if (_findTotalMatches == 0)
            {
                FindCounterText.Text = "No matches";
            }
            else
            {
                int current = _findActiveMatchIndex + 1;
                string refSuffix = "";
                if (_findActiveMatchIndex >= 0 && _findActiveMatchIndex < _findMatchList.Count)
                {
                    var m = _findMatchList[_findActiveMatchIndex];
                    if (m.TryGetProperty("reference", out var r) && !string.IsNullOrEmpty(r.GetString()))
                    {
                        refSuffix = $" ({r.GetString()})";
                    }
                }
                FindCounterText.Text = $"{current} of {_findTotalMatches}{refSuffix}";
            }
        }

        private void FindNext()
        {
            if (_findTotalMatches == 0) return;
            int next = (_findActiveMatchIndex + 1) % _findTotalMatches;
            SetActiveMatch(next);
        }

        private void FindPrevious()
        {
            if (_findTotalMatches == 0) return;
            int prev = (_findActiveMatchIndex - 1 + _findTotalMatches) % _findTotalMatches;
            SetActiveMatch(prev);
        }

        private async void SetActiveMatch(int index)
        {
            _findActiveMatchIndex = index;
            await ReaderWebView.ExecuteScriptAsync($"reader.setActiveFindMatch({index});");
            UpdateFindCounterUI();
        }

        private void FindNextButton_Click(object sender, RoutedEventArgs e) => FindNext();
        private void FindPrevButton_Click(object sender, RoutedEventArgs e) => FindPrevious();

        // =========================================================================
        // Fullscreen Temple Recitation / Zen Mode (Milestone Pillar 4)
        // =========================================================================

        private bool _isZenMode;

        private void ZenAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            ToggleZenMode();
            args.Handled = true;
        }

        private async void BreadcrumbSegmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement anchor || anchor.Tag is not BreadcrumbSegment segment) return;
            await ShowBreadcrumbNavigationFlyoutAsync(segment, anchor);
        }

        private async Task ShowBreadcrumbNavigationFlyoutAsync(BreadcrumbSegment segment, FrameworkElement anchor)
        {
            try
            {
                var bookKey = ViewModel.CurrentRecord?.BookKey;
                if (string.IsNullOrEmpty(bookKey)) return;

                var hierarchy = await ViewModel.Repository.GetLibraryHierarchyAsync();
                var bookNode = hierarchy.FirstOrDefault(b => b.BookKey.Equals(bookKey, StringComparison.OrdinalIgnoreCase));
                if (bookNode == null) return;

                var flyout = new Flyout();
                var rootPanel = new StackPanel { Width = 310, MaxHeight = 440, Spacing = 8 };

                var headerText = new TextBlock
                {
                    Text = segment.IsLast ? "Jump to Verse" : "Jump to Chapter / Section",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 14
                };
                rootPanel.Children.Add(headerText);

                var searchBox = new TextBox
                {
                    PlaceholderText = "Type to filter...",
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                rootPanel.Children.Add(searchBox);

                var itemsListView = new ListView
                {
                    SelectionMode = ListViewSelectionMode.None,
                    IsItemClickEnabled = true,
                    MaxHeight = 320
                };

                // Populate based on whether segment is book level (Index == 0), verse level (IsLast), or chapter level
                if (segment.Index == 0)
                {
                    headerText.Text = "Jump to Book";

                    void PopulateBooks(string filter)
                    {
                        var filtered = string.IsNullOrWhiteSpace(filter)
                            ? hierarchy.Where(b => !b.IsHeader && !b.IsFolder).ToList()
                            : hierarchy.Where(b => !b.IsHeader && !b.IsFolder && b.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                        itemsListView.ItemsSource = filtered.Select(b => new BreadcrumbNavTarget
                        {
                            Title = b.Title,
                            SubTitle = b.Chapters.Count > 0 ? $"{b.Chapters.Count} chapters / sections" : "Book",
                            RecordKey = b.Chapters.FirstOrDefault()?.Records.FirstOrDefault()?.RecordKey ?? ""
                        }).Where(x => !string.IsNullOrEmpty(x.RecordKey)).ToList();
                    }

                    PopulateBooks("");
                    searchBox.TextChanged += (s, ev) => PopulateBooks(searchBox.Text);
                }
                else if (segment.IsLast && bookNode.Chapters.Count > 0)
                {
                    headerText.Text = "Jump to Verse";

                    var currentRecKey = ViewModel.CurrentRecord?.RecordKey ?? "";
                    var currentChapter = bookNode.Chapters.FirstOrDefault(c => c.Records.Any(r => r.RecordKey == currentRecKey))
                                         ?? bookNode.Chapters.FirstOrDefault();

                    var recordsToDisplay = currentChapter != null ? currentChapter.Records : new List<RecordNode>();

                    void PopulateVerses(string filter)
                    {
                        var filtered = string.IsNullOrWhiteSpace(filter)
                            ? recordsToDisplay
                            : recordsToDisplay.Where(r => r.Reference.Contains(filter, StringComparison.OrdinalIgnoreCase) || r.RecordKey.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                        itemsListView.ItemsSource = filtered.Select(r => new BreadcrumbNavTarget
                        {
                            Title = r.Reference,
                            SubTitle = r.RecordKey,
                            RecordKey = r.RecordKey
                        }).ToList();
                    }

                    PopulateVerses("");
                    searchBox.TextChanged += (s, ev) => PopulateVerses(searchBox.Text);
                }
                else
                {
                    headerText.Text = "Jump to Chapter / Section";

                    void PopulateChapters(string filter)
                    {
                        var filtered = string.IsNullOrWhiteSpace(filter)
                            ? bookNode.Chapters
                            : bookNode.Chapters.Where(c => c.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                        itemsListView.ItemsSource = filtered.Select(c => new BreadcrumbNavTarget
                        {
                            Title = c.Title,
                            SubTitle = c.HasMultipleRecords ? $"{c.Records.Count} verses" : "Chapter reading",
                            RecordKey = c.Records.FirstOrDefault()?.RecordKey ?? ""
                        }).Where(x => !string.IsNullOrEmpty(x.RecordKey)).ToList();
                    }

                    PopulateChapters("");
                    searchBox.TextChanged += (s, ev) => PopulateChapters(searchBox.Text);
                }

                itemsListView.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
                    <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                        <Grid Padding=""4,4"" Margin=""0,2"">
                            <Grid.RowDefinitions>
                                <RowDefinition Height=""Auto"" />
                                <RowDefinition Height=""Auto"" />
                            </Grid.RowDefinitions>
                            <TextBlock Text=""{Binding Title}"" FontWeight=""SemiBold"" FontSize=""13"" TextWrapping=""NoWrap"" TextTrimming=""CharacterEllipsis"" />
                            <TextBlock Grid.Row=""1"" Text=""{Binding SubTitle}"" FontSize=""11"" Foreground=""{ThemeResource TextFillColorSecondaryBrush}"" />
                        </Grid>
                    </DataTemplate>");

                itemsListView.ItemClick += async (s, ev) =>
                {
                    flyout.Hide();
                    if (ev.ClickedItem is BreadcrumbNavTarget target && !string.IsNullOrEmpty(target.RecordKey))
                    {
                        await ViewModel.LoadRecordAsync(target.RecordKey);
                    }
                };

                rootPanel.Children.Add(itemsListView);
                flyout.Content = rootPanel;
                flyout.ShowAt(anchor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Breadcrumb Nav Error] {ex}");
            }
        }

        private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainPage.Current != null)
            {
                MainPage.Current.ToggleLibraryPane();
            }
        }

        private void ZenModeToggleButton_Click(object sender, RoutedEventArgs e) => ToggleZenMode();

        public async void ToggleZenMode(bool? forceZen = null)
        {
            _isZenMode = forceZen ?? !_isZenMode;

            // Hide TopNavGrid completely in Focus/Zen Mode for distraction-free reading
            TopNavGrid.Visibility = _isZenMode ? Visibility.Collapsed : Visibility.Visible;
            if (FloatingZenExitButton != null)
            {
                FloatingZenExitButton.Visibility = _isZenMode ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ZenModeIcon != null && ZenModeToggleButton != null)
            {
                ZenModeIcon.Glyph = _isZenMode ? "\uE73F" : "\uE740";
                ToolTipService.SetToolTip(ZenModeToggleButton, _isZenMode ? "Exit Focus View (Esc)" : "Focus View (F11)");
            }

            if (_isZenMode && _isSplitViewOpen)
            {
                CloseSplitView();
            }

            if (_isZenMode)
            {
                MainPage.Current?.EnterFocusMode(this);
            }
            else
            {
                MainPage.Current?.ExitFocusMode();
            }

            if (_isWebReady && ReaderWebView?.CoreWebView2 != null)
            {
                await ReaderWebView.ExecuteScriptAsync($"reader.setZenMode({(_isZenMode ? "true" : "false")});");
            }
        }

        public void ExitZenMode() => ToggleZenMode(false);

        private void ZoomInAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            ViewModel.ZoomIn();
            SyncFontSizesToWeb();
            args.Handled = true;
        }

        private void ZoomOutAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            ViewModel.ZoomOut();
            SyncFontSizesToWeb();
            args.Handled = true;
        }

        private void ZoomResetAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            ViewModel.ResetZoom();
            SyncFontSizesToWeb();
            args.Handled = true;
        }

        private void EscapeAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (_isZenMode)
            {
                ExitZenMode();
                args.Handled = true;
            }
            else if (FindBarCard.Visibility == Visibility.Visible)
            {
                CloseFindBar();
                args.Handled = true;
            }
        }

        // =========================================================================
        // Side-by-Side Dual Pane Reading & Active Notebook (Milestone Pillar 1)
        // =========================================================================

        private bool _isSplitViewOpen;
        private bool _isParallelWebReady;
        private CorpusRecord? _parallelCurrentRecord;
        private Task? _initParallelWebViewTask;

        private void SplitAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            ToggleSplitView();
            args.Handled = true;
        }

        private void SplitViewToggleButton_Click(object sender, RoutedEventArgs e) => ToggleSplitView();
        private void CloseSplitViewBtn_Click(object sender, RoutedEventArgs e) => CloseSplitView();
        public void CloseSplitView() => ToggleSplitView(false);

        public async void ToggleSplitView(bool? forceOpen = null)
        {
            bool open = forceOpen ?? !_isSplitViewOpen;
            _isSplitViewOpen = open;
            if (open)
            {
                if (_isZenMode) ExitZenMode();
                SplitterBorder.Visibility = Visibility.Visible;
                RightPaneContainer.Visibility = Visibility.Visible;
                LeftPaneCol.Width = new GridLength(1, GridUnitType.Star);
                RightPaneCol.Width = new GridLength(1, GridUnitType.Star);

                if (ParallelScripturePanel.Visibility == Visibility.Visible)
                {
                    await EnsureParallelWebViewInitializedAsync();
                    if (_parallelCurrentRecord == null)
                    {
                        await LoadDefaultParallelRecordAsync();
                    }
                }
                else
                {
                    PopulateSideNotebookForCurrentVerse();
                }
            }
            else
            {
                SplitterBorder.Visibility = Visibility.Collapsed;
                RightPaneContainer.Visibility = Visibility.Collapsed;
                RightPaneCol.Width = new GridLength(0);
                LeftPaneCol.Width = new GridLength(1, GridUnitType.Star);
            }
        }

        private async void ParallelScriptureTabBtn_Click(object sender, RoutedEventArgs e)
        {
            ParallelScriptureTabBtn.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            SideNotebookTabBtn.Style = (Style)Application.Current.Resources["TextBlockButtonStyle"];
            ParallelTabLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            SideNotebookTabLabel.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            ParallelScripturePanel.Visibility = Visibility.Visible;
            SideNotebookPanel.Visibility = Visibility.Collapsed;

            await EnsureParallelWebViewInitializedAsync();
            if (_parallelCurrentRecord == null)
            {
                await LoadDefaultParallelRecordAsync();
            }
        }

        private void SideNotebookTabBtn_Click(object sender, RoutedEventArgs e)
        {
            SideNotebookTabBtn.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            ParallelScriptureTabBtn.Style = (Style)Application.Current.Resources["TextBlockButtonStyle"];
            SideNotebookTabLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            ParallelTabLabel.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            SideNotebookPanel.Visibility = Visibility.Visible;
            ParallelScripturePanel.Visibility = Visibility.Collapsed;

            PopulateSideNotebookForCurrentVerse();
        }

        private Task EnsureParallelWebViewInitializedAsync()
        {
            if (_initParallelWebViewTask == null || _initParallelWebViewTask.IsFaulted ||
                (_initParallelWebViewTask.IsCompleted && !_isParallelWebReady && ParallelWebView?.CoreWebView2 == null))
            {
                _initParallelWebViewTask = InitializeParallelWebViewInternalAsync();
            }
            return _initParallelWebViewTask;
        }

        private async Task InitializeParallelWebViewInternalAsync()
        {
            if (ParallelWebView == null) return;
            if (_isParallelWebReady && ParallelWebView.CoreWebView2 != null) return;

            try
            {
                string assetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Reader");
                var env = await GetSharedEnvironmentAsync();
                await ParallelWebView.EnsureCoreWebView2Async(env);
                var coreWebView2 = ParallelWebView.CoreWebView2;
                if (coreWebView2 == null) return;

                coreWebView2.SetVirtualHostNameToFolderMapping(
                    "reader.local",
                    assetsFolder,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                coreWebView2.WebMessageReceived += async (s, e) =>
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                        string action = doc.RootElement.GetProperty("action").GetString() ?? "";
                        if (action == "ready")
                        {
                            _isParallelWebReady = true;
                            SyncParallelTheme();
                            if (_parallelCurrentRecord != null)
                            {
                                await RenderParallelRecordAsync(_parallelCurrentRecord);
                            }
                        }
                        else if (action == "concordance_lookup")
                        {
                            string word = doc.RootElement.GetProperty("word").GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(word))
                            {
                                await OpenConcordanceDialogAsync(word);
                            }
                        }
                    }
                    catch { }
                };

                coreWebView2.Navigate("https://reader.local/reader.html");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParallelWebView] Init Error: {ex}");
            }
        }

        private async Task LoadDefaultParallelRecordAsync()
        {
            string key = ViewModel.CurrentRecord?.RecordKey ?? "BG-1-1";
            string targetKey = key.Equals("BG-1-1", StringComparison.OrdinalIgnoreCase) ? "BG-2-13" : key;
            await LoadParallelRecordAsync(targetKey);
        }

        private async Task LoadParallelRecordAsync(string queryOrKey)
        {
            try
            {
                ParallelLoadingRing.Visibility = Visibility.Visible;
                string? resolvedKey = await App.Current.ReferenceService.TryResolveExactAsync(queryOrKey);
                resolvedKey ??= queryOrKey;

                var record = await App.Current.Repository.GetRecordAsync(resolvedKey);

                if (record != null)
                {
                    _parallelCurrentRecord = record;
                    ParallelVerseSuggestBox.Text = !string.IsNullOrWhiteSpace(record.Reference) ? record.Reference : record.RecordKey;
                    await RenderParallelRecordAsync(record);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Parallel] Load error: {ex}");
            }
            finally
            {
                ParallelLoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async Task RenderParallelRecordAsync(CorpusRecord record)
        {
            if (!_isParallelWebReady || ParallelWebView?.CoreWebView2 == null) return;
            try
            {
                SyncParallelTheme();
                var options = new
                {
                    showTransliteration = ViewModel.ShowTransliteration,
                    showSynonyms = ViewModel.ShowSynonyms,
                    showPurport = ViewModel.ShowPurport,
                    showPronunciationGuide = ViewModel.ShowPronunciationGuide
                };
                string optionsJson = JsonSerializer.Serialize(options);
                string recordJson = JsonSerializer.Serialize(record);

                string script = $@"
                    try {{
                        reader.renderVerse(JSON.parse({JsonSerializer.Serialize(recordJson)}), JSON.parse({JsonSerializer.Serialize(optionsJson)}), [], []);
                        'OK';
                    }} catch (e) {{
                        'RENDER_ERROR: ' + e.message;
                    }}";
                await ParallelWebView.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Parallel] Render error: {ex}");
            }
        }

        private async void SyncParallelTheme()
        {
            if (!_isParallelWebReady || ParallelWebView?.CoreWebView2 == null) return;
            try
            {
                string theme = this.ActualTheme == ElementTheme.Dark ? "dark" : "light";
                await ParallelWebView.ExecuteScriptAsync($"reader.setTheme('{theme}');");
            }
            catch { }
        }

        private async void ParallelPrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_parallelCurrentRecord == null) return;
            string? prev = await App.Current.Repository.GetAdjacentRecordKeyAsync(_parallelCurrentRecord.RecordKey, false);
            if (!string.IsNullOrEmpty(prev)) await LoadParallelRecordAsync(prev);
        }

        private async void ParallelNextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_parallelCurrentRecord == null) return;
            string? next = await App.Current.Repository.GetAdjacentRecordKeyAsync(_parallelCurrentRecord.RecordKey, true);
            if (!string.IsNullOrEmpty(next)) await LoadParallelRecordAsync(next);
        }

        private async void ParallelVerseSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string query = args.QueryText?.Trim() ?? "";
            if (!string.IsNullOrEmpty(query))
            {
                await LoadParallelRecordAsync(query);
            }
        }

        private void PopulateSideNotebookForCurrentVerse()
        {
            string rk = ViewModel.CurrentRecord?.RecordKey ?? "General";
            string refStr = !string.IsNullOrWhiteSpace(ViewModel.CurrentRecord?.Reference) ? ViewModel.CurrentRecord.Reference : rk;
            SideNoteTitleBox.PlaceholderText = $"Realization on {refStr}...";
            SideNoteStatusText.Text = $"Linked to {refStr}";

            var existing = ViewModel.Notes.FirstOrDefault(n => n.RecordKey == rk);
            if (existing != null)
            {
                SideNoteTitleBox.Text = existing.Title ?? "";
                SideNoteContentBox.Text = existing.Content ?? "";
                SideNoteStatusText.Text = $"Editing note saved {existing.UpdatedUtc.ToLocalTime():g}";
            }
            else
            {
                SideNoteTitleBox.Text = "";
                SideNoteContentBox.Text = "";
            }
        }

        private void QuickTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string tag)
            {
                string current = SideNoteContentBox.Text;
                if (string.IsNullOrWhiteSpace(current))
                {
                    SideNoteContentBox.Text = tag + " ";
                }
                else if (!current.Contains(tag, StringComparison.OrdinalIgnoreCase))
                {
                    SideNoteContentBox.Text = current.TrimEnd() + " " + tag + " ";
                }
                SideNoteContentBox.SelectionStart = SideNoteContentBox.Text.Length;
                SideNoteContentBox.Focus(FocusState.Programmatic);
            }
        }

        private void SideNoteClearBtn_Click(object sender, RoutedEventArgs e)
        {
            SideNoteTitleBox.Text = "";
            SideNoteContentBox.Text = "";
            SideNoteStatusText.Text = "Cleared";
        }

        private async void SideNoteSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string title = SideNoteTitleBox.Text.Trim();
            string content = SideNoteContentBox.Text.Trim();
            string rk = ViewModel.CurrentRecord?.RecordKey ?? "General";

            if (string.IsNullOrWhiteSpace(content))
            {
                SideNoteStatusText.Text = "Cannot save empty realization";
                return;
            }

            try
            {
                var saved = await ViewModel.SaveOrUpdateNoteAsync(null, rk, content, string.IsNullOrEmpty(title) ? null : title);
                if (saved != null)
                {
                    SideNoteStatusText.Text = $"Saved at {DateTime.Now:HH:mm:ss}";
                    if (_isWebReady && ReaderWebView?.CoreWebView2 != null)
                    {
                        string savedJson = JsonSerializer.Serialize(saved);
                        await ReaderWebView.ExecuteScriptAsync($"reader.onNoteSaved(JSON.parse({JsonSerializer.Serialize(savedJson)}));");
                    }
                }
            }
            catch (Exception ex)
            {
                SideNoteStatusText.Text = $"Error: {ex.Message}";
            }
        }
    }

    public record SearchResultNavigationTarget(string RecordKey, string SearchQuery);

    public class BreadcrumbNavTarget
    {
        public string Title { get; set; } = string.Empty;
        public string SubTitle { get; set; } = string.Empty;
        public string RecordKey { get; set; } = string.Empty;
    }
}
