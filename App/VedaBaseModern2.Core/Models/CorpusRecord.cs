using System;
using System.Collections.Generic;

namespace VedaBaseModern.Core.Models
{
    public class CorpusRecord
    {
        public string RecordKey { get; set; } = string.Empty;
        public string BookKey { get; set; } = string.Empty;
        public int Sequence { get; set; }
        public string? ParentKey { get; set; }
        public string RecordType { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public string ReferenceStatus { get; set; } = string.Empty;
        public string? Title { get; set; }
        
        private string _devanagari = string.Empty;
        private string? _normalizedDevanagari;

        public string Devanagari
        {
            get
            {
                if (_normalizedDevanagari == null)
                {
                    _normalizedDevanagari = DevanagariNormalizer.Normalize(_devanagari);
                }
                return _normalizedDevanagari;
            }
            set
            {
                _devanagari = value;
                _normalizedDevanagari = null;
            }
        }

        public string RawDevanagari => _devanagari;
        public string Transliteration { get; set; } = string.Empty;
        public string Synonyms { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public string Purports { get; set; } = string.Empty;

        // Helper properties for UI
        public bool HasDevanagari => !string.IsNullOrWhiteSpace(Devanagari);
        public bool HasTransliteration => !string.IsNullOrWhiteSpace(Transliteration);
        public bool HasSynonyms => !string.IsNullOrWhiteSpace(Synonyms);
        public bool HasTranslation => !string.IsNullOrWhiteSpace(Translation);
        public bool HasPurports => !string.IsNullOrWhiteSpace(Purports);

        /// <summary>
        /// Continuous flowing prose translation with hard-wrap artifacts removed.
        /// </summary>
        public string CleanTranslation => VedaBaseModern.Core.Services.ProseFormatter.CleanProse(Translation);

        // UI-friendly split purports since FTS requires them as a single string, 
        // but the UI typically renders them as separate paragraphs.
        public IReadOnlyList<string> PurportParagraphs => 
            VedaBaseModern.Core.Services.ProseFormatter.GetCleanParagraphs(Purports);
    }

    public class BookNode
    {
        public string BookKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = "His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda";
        public string Category { get; set; } = string.Empty;
        public bool IsHeader { get; set; } = false;
        public bool IsPdf { get; set; } = false;
        public string? PdfPath { get; set; }

        public bool IsOtherAuthor => !string.IsNullOrWhiteSpace(Author) &&
            !Author.Contains("Prabhupāda", StringComparison.OrdinalIgnoreCase) &&
            !Author.Contains("Bhaktivedanta Swami", StringComparison.OrdinalIgnoreCase);

        public bool IsImported => IsPdf || BookKey.StartsWith("PDF_", StringComparison.OrdinalIgnoreCase) || IsOtherAuthor;

        public bool IsFolder { get; set; } = false;
        public List<BookNode> Children { get; set; } = new List<BookNode>();

        public List<ChapterNode> Chapters { get; set; } = new List<ChapterNode>();
    }

    public class ChapterNode
    {
        public string Title { get; set; } = string.Empty;
        public List<RecordNode> Records { get; set; } = new List<RecordNode>();

        // Prose-book chapters carry exactly one record (the chapter/section IS
        // the record); scripture chapters carry many verses. Used by the UI to
        // hide the "N verses" label for the single-record case rather than
        // showing the awkward "1 verses". A plain bool (not Visibility) so
        // this Core model stays free of any WinUI/platform dependency.
        public bool HasMultipleRecords => Records.Count > 1;
    }

    public class RecordNode
    {
        public string RecordKey { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public int Sequence { get; set; }
    }
}
