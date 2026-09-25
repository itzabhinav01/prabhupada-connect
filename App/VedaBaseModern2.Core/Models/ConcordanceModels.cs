using System.Collections.Generic;

namespace VedaBaseModern.Core.Models
{
    public class ConcordanceMatch
    {
        public string RecordKey { get; set; } = string.Empty;
        public string BookKey { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public string Gloss { get; set; } = string.Empty;
    }

    public class ConcordanceBookGroup
    {
        public string BookTitle { get; set; } = string.Empty;
        public string BookKey { get; set; } = string.Empty;
        public List<ConcordanceMatch> Matches { get; set; } = new();
        public int Count => Matches.Count;
    }

    public class ConcordanceResult
    {
        public string QueryWord { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public List<ConcordanceBookGroup> BookGroups { get; set; } = new();
        public List<ConcordanceMatch> AllMatches { get; set; } = new();
    }
}
