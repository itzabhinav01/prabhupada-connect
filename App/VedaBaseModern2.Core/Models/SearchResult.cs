namespace VedaBaseModern.Core.Models
{
    public class SearchResult
    {
        public string RecordKey { get; set; } = string.Empty;
        public string BookKey { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string Category { get; set; } = "Scripture";

        // Corpus Sequence - the deterministic canonical ordering key within a
        // book (see MILESTONE_2 hierarchy work). Used to sort verse search
        // results into scriptural order rather than pure relevance order.
        // 0 for non-scripture results (Notes/Bookmarks), which are never
        // canonically resorted.
        public int Sequence { get; set; }

        // Set to true if this result is an exact reference/citation match for the user's query
        public bool IsExactMatch { get; set; }

        // Canonical ordering priority tier (1 = reference, 2 = pratika verse opening, 3 = synonyms opening, 4 = verse text, 5 = translation, 6 = purport)
        public int MatchTier { get; set; } = 6;


        // Phase 4.9.4: only populated for Category == "Highlight" - lets a
        // search-result click navigate to the exact highlighted passage
        // (Field + character range) instead of just the chapter start, and
        // lets the result card show the highlight's persisted color.
        public string? Field { get; set; }
        public int StartOffset { get; set; } = -1;
        public int Length { get; set; } = -1;
        public HighlightColor? HighlightColor { get; set; }

        public bool HasHighlightColor => HighlightColor.HasValue;
        public string HighlightColorDisplayName => HighlightColor.HasValue
            ? HighlightColorHelper.GetDisplayName(HighlightColor.Value)
            : string.Empty;
    }
}
