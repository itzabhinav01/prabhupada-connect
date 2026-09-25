using System.Collections.Generic;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Repositories
{
    public interface ICorpusRepository
    {
        Task<List<BookNode>> GetLibraryHierarchyAsync();
        void InvalidateLibraryHierarchyCache();
        Task<CorpusRecord?> GetRecordAsync(string recordKey);

        // Batch lookup used by Milestone 5's Recently Read list so rendering N
        // history rows costs one query instead of N. Read-only, same as every
        // other corpus query - order of the returned list is NOT guaranteed to
        // match recordKeys; callers that need a specific order must re-sort.
        Task<List<CorpusRecord>> GetRecordsAsync(IEnumerable<string> recordKeys);

        // In-memory lookup of the friendly book title for a BookKey (the same
        // mapping GetLibraryHierarchyAsync/SearchAsync already use internally).
        // No database access - safe to call freely from any ViewModel.
        string GetBookTitle(string bookKey);
        Task<string?> GetAdjacentRecordKeyAsync(string currentRecordKey, bool next);
        Task<List<CorpusRecord>> GetChapterRecordsAsync(string recordKey);
        string GetCanonicalChapterHeader(string bookKey, string? reference);
        string GetBreadcrumb(string bookKey, string? reference);
        Task<List<VocabTerm>> GetVocabularyTermsAsync(string prefix, int limit = 60);
        Task<(List<SearchResult> Results, int TotalCount)> SearchAsync(string query, string? bookKey = null, int limit = 50, int offset = 0, string? fieldScope = null, IEnumerable<string>? bookKeys = null, bool isExactWord = false, string sortOrder = "relevance", bool isExactCase = false);
    }


    public class VocabTerm
    {
        public string Term { get; set; } = string.Empty;
        public int DocumentCount { get; set; }
        public int TotalOccurrences { get; set; }

        public VocabTerm() { }

        public VocabTerm(string term, int documentCount, int totalOccurrences)
        {
            Term = term;
            DocumentCount = documentCount;
            TotalOccurrences = totalOccurrences;
        }
    }
}
