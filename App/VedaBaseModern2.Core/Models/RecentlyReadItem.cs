using System;

namespace VedaBaseModern.Core.Models
{
    /// <summary>
    /// Display-only merge of a ReadingHistory row (user.db) with its corresponding
    /// corpus record (corpus_v6_final.db), built in memory for the Recently Read
    /// page. Never persisted - no corpus content is ever copied into user.db.
    /// </summary>
    public class RecentlyReadItem
    {
        public string RecordKey { get; set; } = string.Empty;
        public DateTime LastOpenedUtc { get; set; }
        public int OpenCount { get; set; }

        /// <summary>False when RecordKey no longer resolves against the corpus repository.
        /// The underlying ReadingHistory row is preserved either way - this flag only
        /// controls how the item is rendered/whether it is clickable.</summary>
        public bool IsAvailable { get; set; } = true;

        public string BookKey { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string? Title { get; set; }

        public string LastOpenedDisplay => LastOpenedUtc.ToLocalTime().ToString("g");

        // Pure display hint for the list (dim unavailable rows). Kept on the
        // model, not computed via x:Bind function-binding, to avoid the WMC9999
        // x:Bind compiler issues already documented for Milestone 4's ItemsRepeater
        // templates (see MILESTONE_4_IMPLEMENTATION.md).
        public double DisplayOpacity => IsAvailable ? 1.0 : 0.5;
    }
}
