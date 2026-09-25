using System;

namespace VedaBaseModern.Core.Models
{
    /// <summary>
    /// A research note. Always belongs to a verse via RecordKey (a note with
    /// no scripture association at all is not yet supported by the schema -
    /// RecordKey remains required), but the scripture-range anchor
    /// (Field/StartOffset/Length) is optional: null means "a general note
    /// about this verse", not "no note". Title is optional too. Mirrors the
    /// same (RecordKey, Field, StartOffset, Length) anchor shape already used
    /// by Highlight, so a note can reuse the exact same navigate-to-passage
    /// logic (see HighlightNavigationTarget) - SelectedTextPreview is a
    /// read-only snapshot for display only, never the source of truth for
    /// the anchor itself.
    /// </summary>
    public class UserNote
    {
        public string Id { get; set; } = string.Empty;
        // Null = a general research note with no scripture association
        // ("General Research Note", Phase 2: Research Platform) - not an
        // error and not the same as an empty/unset value.
        public string? RecordKey { get; set; }
        public string? Title { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? DeletedUtc { get; set; }

        public bool IsDeleted => DeletedUtc.HasValue;

        // Optional scripture-range anchor - same shape/meaning as
        // Highlight.Field/StartOffset/Length. Null Field or StartOffset < 0
        // means this note has no precise anchor (a general note about the
        // verse as a whole, or a pre-Phase-2 note).
        public string? Field { get; set; }
        public int StartOffset { get; set; } = -1;
        public int Length { get; set; } = -1;

        public bool HasScriptureAnchor => !string.IsNullOrEmpty(Field) && StartOffset >= 0 && Length > 0;

        public string CreatedUtcString => CreatedUtc.ToLocalTime().ToString("g");
    }

    public class UserBookmark
    {
        // Stable identity, independent of RecordKey (Phase 1: Research
        // Platform) - required so a future sync protocol can tell two
        // bookmarks apart even if they happen to target the same verse from
        // different devices/points in time.
        public string Id { get; set; } = string.Empty;
        public string RecordKey { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? DeletedUtc { get; set; }

        public bool IsDeleted => DeletedUtc.HasValue;
        // Null = not yet filed into any collection ("uncategorized"), not an error.
        public string? CollectionId { get; set; }
        public string? Title { get; set; }
    }

    /// <summary>
    /// A user-defined bookmark grouping ("Daily Reading", "Research - Bhakti").
    /// Purely organizational - never references corpus content directly, only
    /// grouped by the Bookmarks that point at it via CollectionId.
    /// Since Phase 3 (Migration 10), collections have UpdatedUtc and DeletedUtc
    /// to support soft-deletion (tombstoning) and conflict-safe multi-device sync.
    /// </summary>
    public class BookmarkCollection
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? DeletedUtc { get; set; }
        public int SortOrder { get; set; }

        public bool IsDeleted => DeletedUtc.HasValue;
    }

    /// <summary>
    /// Key-value synchronization metadata (Phase 3: Migration 10) storing local
    /// device identity, sync high-water marks, schema versions, and cloud state.
    /// </summary>
    public class SyncMetadataEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>
    /// One of a small fixed set of highlight colors. Deliberately not a raw
    /// color value - a closed set keeps the annotation system restrained and
    /// theme-consistent (see App.xaml's Highlight* brushes) rather than an
    /// arbitrary color picker.
    /// </summary>
    public enum HighlightColor { Yellow, Green, Blue }

    /// <summary>
    /// A precise, persistent annotation over an EXACT character range within
    /// one displayed content block of one verse (Phase 4.7) - anchored to
    /// (RecordKey, Field, StartOffset, Length), never to screen coordinates
    /// or pixel position. Field identifies which block: "Transliteration",
    /// "Synonyms", "Translation", or "Purport:{index}" for an individual
    /// purport paragraph. StartOffset/Length are plain C# string character
    /// (UTF-16 code unit) indices into that exact corpus field value, as
    /// originally read from the frozen corpus - never a transformed or
    /// re-normalized copy of it, and never anything the corpus itself stores.
    /// A verse may hold many Highlights, including several in the same
    /// Field, as long as their ranges don't overlap (see
    /// ReadingViewModel.RangesOverlap - overlap is rejected, not merged or
    /// silently allowed; a deterministic, documented choice for this phase).
    /// The underlying corpus text is never duplicated or modified by a
    /// Highlight - SelectedText is a read-only display/verification snapshot
    /// only, reconstructible at any time from (RecordKey, Field,
    /// StartOffset, Length) against the live corpus.
    ///
    /// StartOffset/Length can be -1/-1 for a "legacy" highlight created
    /// under the Phase 4.6 whole-block model, before precise offsets
    /// existed (see the 6->7 migration) - the application always checks
    /// IsLegacyBlockLevel before treating StartOffset/Length as real values.
    /// </summary>
    public class Highlight
    {
        public string Id { get; set; } = string.Empty;
        public string RecordKey { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public HighlightColor Color { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? DeletedUtc { get; set; }

        public bool IsDeleted => DeletedUtc.HasValue;
        public int StartOffset { get; set; } = -1;
        public int Length { get; set; } = -1;
        public string? SelectedText { get; set; }

        public bool IsLegacyBlockLevel => StartOffset < 0 || Length <= 0;

        /// <summary>
        /// Deterministic overlap check: returns true if range [aStart, aStart + aLen)
        /// intersects [bStart, bStart + bLen). Adjacent ranges (e.g. [0, 5) and [5, 10))
        /// do NOT overlap.
        /// </summary>
        public static bool RangesOverlap(int aStart, int aLen, int bStart, int bLen)
        {
            if (aLen <= 0 || bLen <= 0) return false;
            int aEnd = aStart + aLen;
            int bEnd = bStart + bLen;
            return aStart < bEnd && bStart < aEnd;
        }

        /// <summary>
        /// Checks if this highlight overlaps with a candidate range.
        /// Legacy block-level highlights do not have precision ranges and return false.
        /// </summary>
        public bool OverlapsWith(int start, int length)
        {
            if (IsLegacyBlockLevel || length <= 0) return false;
            return RangesOverlap(StartOffset, Length, start, length);
        }
    }

    /// <summary>
    /// One row in user.db's ReadingHistory table - "this RecordKey was actually opened
    /// in Reading View, most recently at LastOpenedUtc, OpenCount times total."
    /// One logical row per RecordKey (see ReadingHistory schema in
    /// MILESTONE_5_HISTORY_ARCHITECTURE.md).
    /// </summary>
    public class ReadingHistoryEntry
    {
        public string RecordKey { get; set; } = string.Empty;
        public DateTime LastOpenedUtc { get; set; }
        public int OpenCount { get; set; }
    }

    public class UserSearchResult
    {
        // Null for General Research Notes with no scripture anchor
        public string? RecordKey { get; set; }
        public string SourceType { get; set; } = string.Empty; // "Note", "Bookmark", or "Highlight"
        public string ContentSnippet { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }

        // Phase 4.9.4: only populated for SourceType == "Highlight" - carries
        // exactly what ReadingPage needs to navigate to and select the source
        // passage (not just open the chapter start). Field/StartOffset/Length
        // are the SAME (RecordKey, Field, StartOffset, Length) anchor already
        // used to paint the highlight in Reading View - no new identity model.
        public string? Field { get; set; }
        public int StartOffset { get; set; } = -1;
        public int Length { get; set; } = -1;
        public HighlightColor? Color { get; set; }
    }
}
