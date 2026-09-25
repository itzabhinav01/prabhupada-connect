using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.UI.ViewModels
{
    /// <summary>
    /// One note in the "all notes" Personal Workspace list - the note itself
    /// plus enough corpus context (Reference/BookTitle) to display it without
    /// forcing a click-through just to see which verse it belongs to. A
    /// general research note (no RecordKey) shows a fixed placeholder
    /// instead of invented scripture context.
    /// </summary>
    public class NoteListItem
    {
        public string Id { get; set; } = string.Empty;
        public string? RecordKey { get; set; }
        public string? Title { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public string? Field { get; set; }
        public int StartOffset { get; set; } = -1;
        public int Length { get; set; } = -1;
        public string Reference { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = new();
        public List<string> WikiLinks { get; set; } = new();
        public bool HasTags => Tags.Count > 0;
        public Microsoft.UI.Xaml.Visibility HasTagsVisibility => HasTags ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public string TagsSummary => string.Join("  ", Tags.Select(t => $"#{t}"));

        public bool HasScriptureAnchor => !string.IsNullOrEmpty(RecordKey) && !string.IsNullOrEmpty(Field) && StartOffset >= 0 && Length > 0;
        public bool HasScriptureAssociation => !string.IsNullOrEmpty(RecordKey);
        // Exposed directly (rather than via a page-level converter call) so
        // the ListView's DataTemplate - which x:Binds against this type, not
        // the Page - can use it without an out-of-scope function reference.
        public Microsoft.UI.Xaml.Visibility HasScriptureAssociationVisibility =>
            HasScriptureAssociation ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public string TitleDisplay => string.IsNullOrWhiteSpace(Title) ? "Untitled note" : Title!;
        public string PreviewDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Content)) return string.Empty;

                // 1. Decode HTML entities (e.g. &nbsp;, &amp;, &lt;)
                string text = System.Net.WebUtility.HtmlDecode(Content);

                // 2. Convert block boundaries to spaces so words don't get glued together
                text = System.Text.RegularExpressions.Regex.Replace(
                    text,
                    @"<br\s*/?>|</p>|</div>|</li>|</td>|</tr>|</h1>|</h2>|</h3>",
                    " ",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // 3. Strip all HTML tags
                text = System.Text.RegularExpressions.Regex.Replace(
                    text,
                    @"<[^>]+>",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // 4. Clean Markdown syntax for preview display
                text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*([^*]+)\*\*|__([^_]+)__|==([^=]+)==|~~([^~]+)~~|\*([^*]+)\*", "$1$2$3$4$5");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "$1");

                // 5. Clean up non-breaking spaces and collapse whitespace
                text = text.Replace('\u00a0', ' ');
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                const int max = 160;
                return text.Length <= max ? text : text.Substring(0, max) + "…";
            }
        }
        public string UpdatedDisplay => UpdatedUtc.ToLocalTime().ToString("g");
        public string FieldDisplay => Field switch
        {
            null => string.Empty,
            "Transliteration" => "Transliteration",
            "Translation" => "Translation",
            "Synonyms" => "Synonyms",
            "Devanagari" => "Devanagari",
            var f when f.StartsWith("Purport:") => $"Purport (¶{f.Substring(8)})",
            "Purport" => "Purport",
            _ => Field
        };
    }

    public partial class NotesViewModel : ObservableObject
    {
        private readonly ICorpusRepository _corpusRepository;
        private readonly IUserRepository _userRepository;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusText = "Loading notes...";

        // Unfiltered - kept so search can be recomputed client-side without
        // another database round trip, same pattern as HighlightsViewModel.
        public ObservableCollection<NoteListItem> Notes { get; } = new();

        public ObservableCollection<string> AvailableTags { get; } = new();
        [ObservableProperty] private string _selectedTag = "All";
        [ObservableProperty] private NoteListItem? _selectedNote;

        [ObservableProperty] private string _searchText = string.Empty;
        public ObservableCollection<NoteListItem> FilteredNotes { get; } = new();
        [ObservableProperty] private string _filteredCountText = string.Empty;
        [ObservableProperty] private bool _isFilterEmpty;

        // ---- Editor state (create or edit) ----
        [ObservableProperty] private bool _isEditorOpen;
        [ObservableProperty] private bool _isEditingExisting;
        [ObservableProperty] private string? _editingNoteId;
        [ObservableProperty] private string _editTitle = string.Empty;
        [ObservableProperty] private string _editContent = string.Empty;
        [ObservableProperty] private string? _editRecordKey;
        [ObservableProperty] private string? _editField;
        [ObservableProperty] private int _editStartOffset = -1;
        [ObservableProperty] private int _editLength = -1;
        [ObservableProperty] private string _editSourceBookTitle = string.Empty;
        [ObservableProperty] private string _editSourceReference = string.Empty;
        [ObservableProperty] private bool _editHasSource;
        [ObservableProperty] private string _editorErrorMessage = string.Empty;

        public NotesViewModel(ICorpusRepository corpusRepository, IUserRepository userRepository)
        {
            _corpusRepository = corpusRepository;
            _userRepository = userRepository;
        }

        partial void OnSearchTextChanged(string value)
        {
            if (!IsLoading) ApplyFilter();
        }

        partial void OnSelectedTagChanged(string value)
        {
            if (!IsLoading) ApplyFilter();
        }

        // Pure client-side view filtering - never touches the database,
        // never mutates a note record.
        private void ApplyFilter()
        {
            FilteredNotes.Clear();

            IEnumerable<NoteListItem> filtered = Notes;

            // 1. Tag filter
            if (!string.IsNullOrWhiteSpace(SelectedTag) && !SelectedTag.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                string targetTag = SelectedTag.TrimStart('#');
                filtered = filtered.Where(n => n.Tags.Any(t => string.Equals(t, targetTag, StringComparison.OrdinalIgnoreCase)));
            }

            // 2. Query search
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var query = SearchText.Trim();
                filtered = filtered.Where(n =>
                    (n.Title != null && n.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (n.Content != null && n.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (n.BookTitle != null && n.BookTitle.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (n.Reference != null && n.Reference.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    n.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var n in filtered) FilteredNotes.Add(n);
            FilteredCountText = FilteredNotes.Count == 1 ? "1 note" : $"{FilteredNotes.Count} notes";
            IsFilterEmpty = FilteredNotes.Count == 0;

            if (SelectedNote == null || !FilteredNotes.Contains(SelectedNote))
            {
                SelectedNote = FilteredNotes.FirstOrDefault();
            }
        }

        public async Task LoadNotesAsync()
        {
            IsLoading = true;
            Notes.Clear();
            try
            {
                var all = await _userRepository.GetAllNotesAsync();
                if (all.Count == 0)
                {
                    StatusText = "No notes yet.";
                    ApplyFilter();
                    return;
                }
                StatusText = all.Count == 1 ? "1 Note" : $"{all.Count} Notes";

                var recordCache = new Dictionary<string, CorpusRecord?>();

                foreach (var n in all)
                {
                    string reference = string.Empty;
                    string bookTitle = "General Research Note";

                    if (!string.IsNullOrEmpty(n.RecordKey))
                    {
                        if (!recordCache.TryGetValue(n.RecordKey, out var record))
                        {
                            record = await _corpusRepository.GetRecordAsync(n.RecordKey);
                            recordCache[n.RecordKey] = record;
                        }
                        reference = record != null
                            ? (string.IsNullOrWhiteSpace(record.Reference) ? record.RecordKey : record.Reference)
                            : n.RecordKey;
                        bookTitle = record != null ? _corpusRepository.GetBookTitle(record.BookKey ?? "UNKNOWN") : "Record unavailable";
                    }

                    var tags = ExtractHashtags((n.Title ?? "") + " " + (n.Content ?? ""));
                    var wikiLinks = ExtractWikiLinks(n.Content ?? "");

                    Notes.Add(new NoteListItem
                    {
                        Id = n.Id,
                        RecordKey = n.RecordKey,
                        Title = n.Title,
                        Content = n.Content,
                        CreatedUtc = n.CreatedUtc,
                        UpdatedUtc = n.UpdatedUtc,
                        Field = n.Field,
                        StartOffset = n.StartOffset,
                        Length = n.Length,
                        Reference = reference,
                        BookTitle = bookTitle,
                        Tags = tags,
                        WikiLinks = wikiLinks
                    });
                }

                AvailableTags.Clear();
                AvailableTags.Add("All");
                var distinctTags = Notes.SelectMany(note => note.Tags)
                    .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .ToList();
                foreach (var t in distinctTags) AvailableTags.Add("#" + t);

                ApplyFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Notes] Failed to load notes: {ex}");
                Notes.Clear();
                StatusText = "Couldn't load your notes. Please try again.";
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ---- Editor commands ----

        [RelayCommand]
        public void StartNewNote()
        {
            IsEditingExisting = false;
            EditingNoteId = null;
            EditTitle = string.Empty;
            EditContent = string.Empty;
            EditRecordKey = null;
            EditField = null;
            EditStartOffset = -1;
            EditLength = -1;
            EditSourceBookTitle = string.Empty;
            EditSourceReference = string.Empty;
            EditHasSource = false;
            EditorErrorMessage = string.Empty;
            IsEditorOpen = true;
        }

        [RelayCommand]
        public void OpenNoteForEdit(NoteListItem note)
        {
            if (note == null) return;
            IsEditingExisting = true;
            EditingNoteId = note.Id;
            EditTitle = note.Title ?? string.Empty;
            EditContent = note.Content;
            EditRecordKey = note.RecordKey;
            EditField = note.Field;
            EditStartOffset = note.StartOffset;
            EditLength = note.Length;
            EditSourceBookTitle = note.BookTitle;
            EditSourceReference = note.Reference;
            EditHasSource = note.HasScriptureAssociation;
            EditorErrorMessage = string.Empty;
            IsEditorOpen = true;
        }

        [RelayCommand]
        public void CloseEditor()
        {
            IsEditorOpen = false;
            EditorErrorMessage = string.Empty;
        }

        [RelayCommand]
        public async Task SaveNoteAsync()
        {
            if (string.IsNullOrWhiteSpace(EditContent))
            {
                EditorErrorMessage = "A note needs some content before it can be saved.";
                return;
            }

            try
            {
                if (IsEditingExisting && !string.IsNullOrEmpty(EditingNoteId))
                {
                    await _userRepository.UpdateNoteAsync(EditingNoteId, EditContent, EditTitle);
                }
                else
                {
                    await _userRepository.CreateNoteAsync(EditRecordKey, EditContent, EditTitle, EditField, EditStartOffset, EditLength);
                }
                IsEditorOpen = false;
                EditorErrorMessage = string.Empty;
                await LoadNotesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Notes] Failed to save note: {ex}");
                EditorErrorMessage = "Couldn't save this note. Please try again.";
            }
        }

        [RelayCommand]
        public async Task DeleteEditingNoteAsync()
        {
            if (string.IsNullOrEmpty(EditingNoteId)) return;
            try
            {
                await _userRepository.DeleteNoteAsync(EditingNoteId);
                IsEditorOpen = false;
                await LoadNotesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Notes] Failed to delete note '{EditingNoteId}': {ex}");
                EditorErrorMessage = "Couldn't delete this note. Please try again.";
            }
        }

        public static List<string> ExtractHashtags(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"(?<=#)[a-zA-Z0-9_\-]+");
            return matches.Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
        }

        public static List<string> ExtractWikiLinks(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"(?<=\[\[)[^\]]+(?=\]\])");
            return matches.Select(m => m.Value.Trim()).Distinct().ToList();
        }

        public async Task<string> ExportToObsidianVaultAsync(string destinationZipPath)
        {
            using var zip = System.IO.Compression.ZipFile.Open(destinationZipPath, System.IO.Compression.ZipArchiveMode.Create);
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var note in Notes)
            {
                string baseName = !string.IsNullOrWhiteSpace(note.Title)
                    ? string.Concat(note.Title.Split(System.IO.Path.GetInvalidFileNameChars()))
                    : (!string.IsNullOrWhiteSpace(note.Reference) ? note.Reference : "Untitled Note");

                if (string.IsNullOrWhiteSpace(baseName)) baseName = "Note_" + (note.Id.Length >= 8 ? note.Id.Substring(0, 8) : note.Id);
                string fileName = baseName + ".md";
                int counter = 1;
                while (usedFileNames.Contains(fileName))
                {
                    fileName = $"{baseName}_{counter++}.md";
                }
                usedFileNames.Add(fileName);

                var entry = zip.CreateEntry(fileName, System.IO.Compression.CompressionLevel.Optimal);
                using var writer = new System.IO.StreamWriter(entry.Open());

                // YAML Frontmatter
                await writer.WriteLineAsync("---");
                await writer.WriteLineAsync($"id: \"{note.Id}\"");
                await writer.WriteLineAsync($"title: \"{note.Title?.Replace("\"", "\\\"") ?? ""}\"");
                if (!string.IsNullOrEmpty(note.Reference))
                {
                    await writer.WriteLineAsync($"reference: \"{note.Reference}\"");
                }
                if (!string.IsNullOrEmpty(note.RecordKey))
                {
                    await writer.WriteLineAsync($"recordKey: \"{note.RecordKey}\"");
                }
                if (note.Tags.Count > 0)
                {
                    await writer.WriteLineAsync("tags:");
                    foreach (var tag in note.Tags)
                    {
                        await writer.WriteLineAsync($"  - {tag}");
                    }
                }
                await writer.WriteLineAsync($"created: {note.CreatedUtc:O}");
                await writer.WriteLineAsync($"updated: {note.UpdatedUtc:O}");
                await writer.WriteLineAsync("---");
                await writer.WriteLineAsync();

                if (!string.IsNullOrEmpty(note.Title))
                {
                    await writer.WriteLineAsync($"# {note.Title}");
                    await writer.WriteLineAsync();
                }
                if (!string.IsNullOrEmpty(note.Reference))
                {
                    await writer.WriteLineAsync($"> **Scripture Reference**: [[{note.Reference}]] ({note.BookTitle})");
                    await writer.WriteLineAsync();
                }

                // Clean markdown body
                string mdContent = ConvertHtmlToMarkdown(note.Content);
                await writer.WriteAsync(mdContent);
            }

            return destinationZipPath;
        }

        public string GeneratePrintableHtml(NoteListItem? singleNote = null)
        {
            var notesToPrint = singleNote != null ? new List<NoteListItem> { singleNote } : Notes.ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            sb.AppendLine("<title>VedaBase Modern Realizations & Notes</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', serif; margin: 40px; color: #111; line-height: 1.6; }");
            sb.AppendLine(".note { margin-bottom: 40px; page-break-inside: avoid; border-bottom: 1px solid #ccc; padding-bottom: 20px; }");
            sb.AppendLine(".title { font-size: 20px; font-weight: bold; color: #b45309; margin-bottom: 4px; }");
            sb.AppendLine(".meta { font-size: 13px; color: #666; margin-bottom: 12px; }");
            sb.AppendLine(".tag { background: #fef3c7; color: #92400e; padding: 2px 6px; border-radius: 4px; font-size: 11px; margin-right: 4px; font-weight: bold; }");
            sb.AppendLine(".content { font-size: 15px; margin-top: 10px; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; margin: 12px 0; }");
            sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            sb.AppendLine("th { background: #f9f9f9; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>VedaBase Modern - Research Realizations & Notes</h1>");
            sb.AppendLine($"<p style='color:#666;'>Exported on {DateTime.Now:f} • {notesToPrint.Count} notes</p><hr/>");

            foreach (var note in notesToPrint)
            {
                sb.AppendLine("<div class='note'>");
                sb.AppendLine($"<div class='title'>{System.Net.WebUtility.HtmlEncode(note.TitleDisplay)}</div>");
                sb.AppendLine($"<div class='meta'>{System.Net.WebUtility.HtmlEncode(note.BookTitle)} • {System.Net.WebUtility.HtmlEncode(note.Reference)} • Updated {note.UpdatedDisplay}</div>");
                if (note.Tags.Count > 0)
                {
                    sb.AppendLine("<div>");
                    foreach (var t in note.Tags) sb.AppendLine($"<span class='tag'>#{System.Net.WebUtility.HtmlEncode(t)}</span>");
                    sb.AppendLine("</div>");
                }
                sb.AppendLine($"<div class='content'>{note.Content}</div>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        public static string ConvertHtmlToMarkdown(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            string text = html
                .Replace("&amp;nbsp;", " ")
                .Replace("&nbsp;", " ")
                .Replace("<br>", "\n")
                .Replace("<br/>", "\n")
                .Replace("<br />", "\n")
                .Replace("</p>", "\n\n")
                .Replace("<p>", "")
                .Replace("<strong>", "**")
                .Replace("</strong>", "**")
                .Replace("<b>", "**")
                .Replace("</b>", "**")
                .Replace("<em>", "*")
                .Replace("</em>", "*")
                .Replace("<i>", "*")
                .Replace("</i>", "*")
                .Replace("<u>", "__")
                .Replace("</u>", "__");

            text = System.Text.RegularExpressions.Regex.Replace(text, @"<mark[^>]*>(.*?)</mark>", "==$1==");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<a[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", "[$2]($1)");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<li>(.*?)</li>", "- $1\n");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"</?[^>]+(>|$)", "");
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }
    }
}
