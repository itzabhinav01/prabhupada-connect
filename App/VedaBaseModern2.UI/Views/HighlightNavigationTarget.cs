namespace VedaBaseModern.UI.Views
{
    /// <summary>
    /// Phase 4.9.4: Frame.Navigate(typeof(ReadingPage), ...) parameter used
    /// when the caller knows not just WHICH verse to open but the exact
    /// highlighted passage within it (a highlight search result, or a
    /// Highlights-workspace card) - the same (RecordKey, Field, StartOffset,
    /// Length) anchor already used everywhere else a highlight is addressed.
    /// Plain RecordKey-string navigation (every other caller) is untouched.
    /// </summary>
    public sealed record HighlightNavigationTarget(string RecordKey, string Field, int StartOffset, int Length);
}
