using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class HelpGuidePage : Page
    {
        private readonly List<Expander> _allExpanders = new();

        private const string UniversalAiBookPrompt = @"You are the interactive Vedic Scriptures & Spiritual Book Digitization Assistant for VedaBase Modern.

When the user gives you this prompt, DO NOT immediately output random JSON. Instead, first warmly greet the user and ask these 5 essential onboarding questions to configure the ingestion:

1. 📖 Book Title & Author: What is the full title of the spiritual book, and who is the revered author? (e.g. Śrīla Bhaktivinoda Ṭhākura, Śrīla Rūpa Gosvāmī, Śrīla Sanātana Gosvāmī, Śrīla Jīva Gosvāmī, Śrīla Narottama dāsa Ṭhākura, Śrīla Viśvanātha Cakravartī Ṭhākura, or another spiritual author).
2. 📄 Source Document Format: What format will you upload or paste? (PDF, Microsoft Word .docx, RTF, plain text .txt, or EPUB).
3. 📜 Book Structure: Is this a verse scripture (with Sanskrit/Bengali text, IAST Roman transliteration with diacritics, word-for-word synonyms, translation, and purports/commentaries) OR a prose narrative book (chapters, essays, lectures, dialogues)?
4. 🔢 Chapter & Verse Numbering: How would you like the chapters and verses keyed? (e.g. JD-1.1 for Jaiva Dharma, BRS-1.1.1 for Bhakti-rasāmṛta-sindhu, or standard Chapter 1, 2, 3).
5. 📚 Library Section: Is this a work by Śrīla Prabhupāda, or should it be placed in the dedicated 'Works by Other Ācāryas & Authors' section of VedaBase Modern?

Once the user answers your questions and uploads/pastes their source text (or chapters), convert the content into the exact VedaBase Modern import JSON schema below:

{
  ""bookKey"": ""SHORT_UNIQUE_KEY_UPPERCASE"",
  ""abbreviation"": ""Short Abbr"",
  ""edition"": ""Standard"",
  ""title"": ""Full Book Title"",
  ""author"": ""Full Revered Author Name"",
  ""category"": ""Other Ācāryas"",
  ""records"": [
    {
      ""recordKey"": ""BOOKKEY-1-1"",
      ""reference"": ""Book Abbr 1.1"",
      ""sequence"": 1,
      ""recordType"": ""Verse"",
      ""referenceStatus"": ""Valid"",
      ""title"": ""Optional Chapter or Verse Title"",
      ""devanagari"": ""Sanskrit or Bengali text in Unicode (optional, omit/null if English prose)"",
      ""transliteration"": ""Unicode IAST Roman transliteration with diacritics (e.g. kṛṣṇa)"",
      ""synonyms"": ""word1—meaning1; word2—meaning2."",
      ""translation"": ""English translation or main paragraph."",
      ""purports"": ""Commentary, purport, or continuous discourse paragraphs.""
    }
  ]
}

Formatting & Ingestion Rules for the AI:
1. Multi-Author Categorization: If the author is someone other than Śrīla Prabhupāda, set 'category' to 'Other Ācāryas' (or 'Works by Other Ācāryas & Authors') and set 'author' to their full revered name.
2. Dialogue Speaker Names: If the text contains conversations or dialogues (e.g. 'Devotee:', 'Prabhupāda:', 'Bob:', 'Dr. Patel:'), format the speaker followed by a colon. VedaBase Modern automatically identifies and bolds speaker names in the reader.
3. For prose books without verses, leave 'devanagari', 'transliteration', and 'synonyms' null or empty strings, and place the main content into 'translation' or 'purports'.
4. Ensure valid JSON escaping (properly escape quotes \"" and newlines \n in Sanskrit, Bengali, and English text).
5. Provide the output as a downloadable or copyable JSON file ready for the in-app 'Import Book from JSON...' button.";

        private const string SupabaseMigrationSql = @"-- ========================================================
-- VedaBase Modern: Cloud Sync Schema for Supabase
-- Run this script in your Supabase Project > SQL Editor
-- ========================================================

-- 1. Create User Notes Table
CREATE TABLE IF NOT EXISTS public.user_notes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    record_key TEXT NOT NULL,
    note_content TEXT NOT NULL,
    tags TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

-- 2. Create User Bookmarks Table
CREATE TABLE IF NOT EXISTS public.user_bookmarks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    record_key TEXT NOT NULL UNIQUE,
    reference TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

-- 3. Create User Highlights Table
CREATE TABLE IF NOT EXISTS public.user_highlights (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    record_key TEXT NOT NULL,
    color_hex TEXT NOT NULL,
    start_offset INTEGER NOT NULL DEFAULT 0,
    length INTEGER NOT NULL DEFAULT 0,
    selected_text TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

-- 4. Enable Row Level Security (RLS)
ALTER TABLE public.user_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_bookmarks ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_highlights ENABLE ROW LEVEL SECURITY;

-- 5. Create Permissive Policies for Personal Sync (Anon / Authenticated)
CREATE POLICY ""Allow all operations for anon"" ON public.user_notes FOR ALL USING (true) WITH CHECK (true);
CREATE POLICY ""Allow all operations for anon"" ON public.user_bookmarks FOR ALL USING (true) WITH CHECK (true);
CREATE POLICY ""Allow all operations for anon"" ON public.user_highlights FOR ALL USING (true) WITH CHECK (true);

-- 6. Create Fast Query Indices
CREATE INDEX IF NOT EXISTS idx_notes_record_key ON public.user_notes(record_key);
CREATE INDEX IF NOT EXISTS idx_notes_updated_at ON public.user_notes(updated_at);
CREATE INDEX IF NOT EXISTS idx_bookmarks_record_key ON public.user_bookmarks(record_key);
CREATE INDEX IF NOT EXISTS idx_highlights_record_key ON public.user_highlights(record_key);";

        public HelpGuidePage()
        {
            this.InitializeComponent();
            Loaded += HelpGuidePage_Loaded;
        }

        private void HelpGuidePage_Loaded(object sender, RoutedEventArgs e)
        {
            _allExpanders.Clear();
            if (ExpanderNav != null) _allExpanders.Add(ExpanderNav);
            if (ExpanderSplit != null) _allExpanders.Add(ExpanderSplit);
            if (ExpanderZen != null) _allExpanders.Add(ExpanderZen);
            if (ExpanderMeter != null) _allExpanders.Add(ExpanderMeter);
            if (ExpanderSearch != null) _allExpanders.Add(ExpanderSearch);
            if (ExpanderLexicon != null) _allExpanders.Add(ExpanderLexicon);
            if (ExpanderNotes != null) _allExpanders.Add(ExpanderNotes);
            if (ExpanderAddBooks != null) _allExpanders.Add(ExpanderAddBooks);
            if (ExpanderSupabase != null) _allExpanders.Add(ExpanderSupabase);
            if (ExpanderShortcuts != null) _allExpanders.Add(ExpanderShortcuts);
            if (ExpanderFAQ != null) _allExpanders.Add(ExpanderFAQ);

            if (AiPromptTextBox != null)
                AiPromptTextBox.Text = UniversalAiBookPrompt;

            if (SupabaseSqlTextBox != null)
                SupabaseSqlTextBox.Text = SupabaseMigrationSql;
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var exp in _allExpanders)
                exp.IsExpanded = true;
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var exp in _allExpanders)
                exp.IsExpanded = false;
        }

        private void SearchFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchFilterBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrEmpty(query))
            {
                foreach (var exp in _allExpanders)
                {
                    exp.Visibility = Visibility.Visible;
                }
                StatusInfoBar.IsOpen = false;
                return;
            }

            int visibleCount = 0;
            var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var exp in _allExpanders)
            {
                string tag = (exp.Tag as string)?.ToLowerInvariant() ?? string.Empty;
                bool match = keywords.All(kw => tag.Contains(kw));

                if (match)
                {
                    exp.Visibility = Visibility.Visible;
                    exp.IsExpanded = true;
                    visibleCount++;
                }
                else
                {
                    exp.Visibility = Visibility.Collapsed;
                }
            }

            if (visibleCount == 0)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                StatusInfoBar.Title = "No matching sections";
                StatusInfoBar.Message = "No topics matched your search filter. Clear the search box to view all sections.";
                StatusInfoBar.IsOpen = true;
            }
            else
            {
                StatusInfoBar.IsOpen = false;
            }
        }

        private void CategoryChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string category)
            {
                SearchFilterBox.Text = string.Empty;

                if (category == "All")
                {
                    foreach (var exp in _allExpanders)
                    {
                        exp.Visibility = Visibility.Visible;
                    }
                    MainScrollViewer.ChangeView(null, 0, null);
                    return;
                }

                Expander? target = category switch
                {
                    "Nav" => ExpanderNav,
                    "Split" => ExpanderSplit,
                    "Zen" => ExpanderZen,
                    "Meter" => ExpanderMeter,
                    "Search" => ExpanderSearch,
                    "Lexicon" => ExpanderLexicon,
                    "Notes" => ExpanderNotes,
                    "AddBooks" => ExpanderAddBooks,
                    "Supabase" => ExpanderSupabase,
                    "Shortcuts" => ExpanderShortcuts,
                    "FAQ" => ExpanderFAQ,
                    _ => null
                };

                if (target != null)
                {
                    foreach (var exp in _allExpanders)
                    {
                        exp.Visibility = Visibility.Visible;
                    }
                    target.IsExpanded = true;
                    target.StartBringIntoView();
                }
            }
        }

        private void CopyAiPrompt_Click(object sender, RoutedEventArgs e)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(UniversalAiBookPrompt);
            Clipboard.SetContent(dataPackage);

            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Title = "Prompt Copied!";
            StatusInfoBar.Message = "Universal AI Book Conversion Prompt copied to clipboard. You can now paste it into ChatGPT, Claude, or Gemini.";
            StatusInfoBar.IsOpen = true;
        }

        private void CopySupabaseSql_Click(object sender, RoutedEventArgs e)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(SupabaseMigrationSql);
            Clipboard.SetContent(dataPackage);

            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Title = "SQL Script Copied!";
            StatusInfoBar.Message = "Supabase SQL migration script copied to clipboard. Paste and run it in your Supabase SQL Editor.";
            StatusInfoBar.IsOpen = true;
        }

        private async void ImportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ImportPdfDialog { XamlRoot = this.XamlRoot };
                VedaBaseModern.UI.Services.CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);
                var dialogResult = await dialog.ShowAsync();
                if (dialogResult == ContentDialogResult.Primary)
                {
                    StatusInfoBar.Severity = InfoBarSeverity.Informational;
                    StatusInfoBar.Title = "Importing PDF Book...";
                    StatusInfoBar.Message = $"Processing '{dialog.BookTitle}'...";
                    StatusInfoBar.IsOpen = true;

                    var result = await App.Current.BookImportService.ImportPdfBookAsync(
                        dialog.SelectedFilePath, dialog.BookTitle, dialog.Author, dialog.Category);

                    if (result.Success)
                    {
                        StatusInfoBar.Severity = InfoBarSeverity.Success;
                        StatusInfoBar.Title = "PDF Book Imported Successfully!";
                        StatusInfoBar.Message = result.Message;
                        StatusInfoBar.IsOpen = true;

                        (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
                        if (MainPage.Current != null)
                        {
                            await MainPage.Current.ViewModel.LoadBooksAsync(true);
                        }
                    }
                    else
                    {
                        StatusInfoBar.Severity = InfoBarSeverity.Error;
                        StatusInfoBar.Title = "PDF Import Failed";
                        StatusInfoBar.Message = result.Message;
                        StatusInfoBar.IsOpen = true;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Error;
                StatusInfoBar.Title = "PDF Import Error";
                StatusInfoBar.Message = $"Unexpected error during PDF import: {ex.Message}";
                StatusInfoBar.IsOpen = true;
            }
        }

        private async void ImportBookButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.ViewMode = PickerViewMode.List;
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".json");

                var file = await picker.PickSingleFileAsync();
                if (file == null)
                    return;

                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                StatusInfoBar.Title = "Importing Book...";
                StatusInfoBar.Message = $"Reading and validating '{file.Name}'...";
                StatusInfoBar.IsOpen = true;

                var result = await App.Current.BookImportService.ImportBookFromFileAsync(file.Path);

                if (result.Success)
                {
                    StatusInfoBar.Severity = InfoBarSeverity.Success;
                    StatusInfoBar.Title = "Book Imported Successfully!";
                    StatusInfoBar.Message = result.Message;
                    StatusInfoBar.IsOpen = true;

                    // Refresh Library in MainWindow/MainPage
                    (App.Current.Repository as VedaBaseModern.Core.Repositories.SqliteCorpusRepository)?.InvalidateLibraryHierarchyCache();
                    if (MainPage.Current != null)
                    {
                        await MainPage.Current.ViewModel.LoadBooksAsync(true);
                    }
                }
                else
                {
                    StatusInfoBar.Severity = InfoBarSeverity.Error;
                    StatusInfoBar.Title = "Import Failed";
                    StatusInfoBar.Message = result.Message;
                    StatusInfoBar.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Error;
                StatusInfoBar.Title = "Import Error";
                StatusInfoBar.Message = $"Unexpected error during import: {ex.Message}";
                StatusInfoBar.IsOpen = true;
            }
        }
    }
}
