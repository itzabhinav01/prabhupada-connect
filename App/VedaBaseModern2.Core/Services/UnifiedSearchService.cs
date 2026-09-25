using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.Core.Services
{
    public class UnifiedSearchResults
    {
        public List<SearchResult> Results { get; set; } = new();
        public int TotalCount => ScriptureCount + NotesCount + BookmarksCount + HighlightsCount;
        public int ScriptureCount { get; set; }
        public int NotesCount { get; set; }
        public int BookmarksCount { get; set; }
        public int HighlightsCount { get; set; }
    }

    public interface IUnifiedSearchService
    {
        Task<UnifiedSearchResults> SearchAsync(string query, string? bookKey = null, int limit = 50, int offset = 0, string? fieldScope = null, IEnumerable<string>? bookKeys = null, bool isExactWord = false, string sortOrder = "relevance", bool isExactCase = false);
    }

    public class UnifiedSearchService : IUnifiedSearchService
    {
        private readonly ICorpusRepository _corpusRepository;
        private readonly IUserRepository _userRepository;

        public UnifiedSearchService(ICorpusRepository corpusRepository, IUserRepository userRepository)
        {
            _corpusRepository = corpusRepository;
            _userRepository = userRepository;
        }

        public async Task<UnifiedSearchResults> SearchAsync(string query, string? bookKey = null, int limit = 50, int offset = 0, string? fieldScope = null, IEnumerable<string>? bookKeys = null, bool isExactWord = false, string sortOrder = "relevance", bool isExactCase = false)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new UnifiedSearchResults();
            }

            var corpusTask = _corpusRepository.SearchAsync(query, bookKey, limit, offset, fieldScope, bookKeys, isExactWord, sortOrder, isExactCase);

            var isAllScope = string.IsNullOrWhiteSpace(fieldScope) || fieldScope.Equals("All", StringComparison.OrdinalIgnoreCase);
            var userTask = isAllScope ? _userRepository.SearchUserContentAsync(query) : Task.FromResult(new List<UserSearchResult>());

            await Task.WhenAll(corpusTask, userTask);

            var (corpusResults, corpusTotal) = await corpusTask;
            var userMatches = await userTask;

            int notesCount = userMatches.Count(m => m.SourceType == "Note");
            int bookmarksCount = userMatches.Count(m => m.SourceType == "Bookmark");
            int highlightsCount = userMatches.Count(m => m.SourceType == "Highlight");

            var unified = new UnifiedSearchResults
            {
                ScriptureCount = corpusTotal,
                NotesCount = notesCount,
                BookmarksCount = bookmarksCount,
                HighlightsCount = highlightsCount
            };

            // First add user matches if any
            if (userMatches.Count > 0)
            {
                var recordKeys = userMatches
                    .Where(m => !string.IsNullOrEmpty(m.RecordKey))
                    .Select(m => m.RecordKey!)
                    .Distinct()
                    .ToList();
                var records = await _corpusRepository.GetRecordsAsync(recordKeys);
                var recordDict = records.ToDictionary(r => r.RecordKey);

                foreach (var match in userMatches)
                {
                    string refText = string.IsNullOrEmpty(match.RecordKey) ? "General Research Note" : match.RecordKey;
                    string bookTitle = match.SourceType switch
                    {
                        "Note" => string.IsNullOrEmpty(match.RecordKey) ? "General Research Note" : "My Personal Note",
                        "Highlight" => "My Highlight",
                        _ => "Saved Bookmark"
                    };
                    string bk = "";

                    if (!string.IsNullOrEmpty(match.RecordKey) && recordDict.TryGetValue(match.RecordKey, out var rec))
                    {
                        refText = string.IsNullOrWhiteSpace(rec.Reference) ? rec.RecordKey : rec.Reference;
                        bk = rec.BookKey;
                        string prefix = match.SourceType switch
                        {
                            "Note" => "Note on ",
                            "Highlight" => "Highlight in ",
                            _ => "Bookmark: "
                        };
                        bookTitle = prefix + _corpusRepository.GetBookTitle(rec.BookKey);
                    }

                    unified.Results.Add(new SearchResult
                    {
                        RecordKey = match.RecordKey ?? string.Empty,
                        BookKey = bk,
                        Reference = refText,
                        BookTitle = bookTitle,
                        Preview = match.ContentSnippet,
                        Category = match.SourceType,
                        Field = match.Field,
                        StartOffset = match.StartOffset,
                        Length = match.Length,
                        HighlightColor = match.Color
                    });
                }
            }

            // Then add scripture results
            foreach (var cr in corpusResults)
            {
                cr.Category = "Scripture";
                unified.Results.Add(cr);
            }

            return unified;
        }
    }
}
