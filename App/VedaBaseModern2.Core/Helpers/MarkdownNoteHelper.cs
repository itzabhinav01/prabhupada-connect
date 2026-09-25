using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VedaBaseModern.Core.Helpers
{
    public static class MarkdownNoteHelper
    {
        private static readonly Regex WikiLinkRegex = new(@"\[\[(.*?)\]\]", RegexOptions.Compiled);

        public static List<string> ExtractWikiLinks(string markdownText)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(markdownText)) return list;

            var matches = WikiLinkRegex.Matches(markdownText);
            foreach (Match m in matches)
            {
                if (m.Groups.Count > 1)
                {
                    string refText = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(refText) && !list.Contains(refText))
                    {
                        list.Add(refText);
                    }
                }
            }
            return list;
        }

        public static bool HasWikiLinks(string markdownText)
        {
            if (string.IsNullOrEmpty(markdownText)) return false;
            return WikiLinkRegex.IsMatch(markdownText);
        }
    }
}
