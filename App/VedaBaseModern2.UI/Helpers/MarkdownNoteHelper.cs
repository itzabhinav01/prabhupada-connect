using System.Collections.Generic;

namespace VedaBaseModern.UI.Helpers
{
    public static class MarkdownNoteHelper
    {
        public static List<string> ExtractWikiLinks(string markdownText)
            => VedaBaseModern.Core.Helpers.MarkdownNoteHelper.ExtractWikiLinks(markdownText);

        public static bool HasWikiLinks(string markdownText)
            => VedaBaseModern.Core.Helpers.MarkdownNoteHelper.HasWikiLinks(markdownText);
    }
}
