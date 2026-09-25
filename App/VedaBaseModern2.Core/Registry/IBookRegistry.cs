using System.Collections.Generic;
using System.Threading.Tasks;

namespace VedaBaseModern.Core.Registry
{
    /// <summary>
    /// Structured metadata for a canonical or supplementary book.
    /// </summary>
    public class BookDescriptor
    {
        public string BookKey { get; init; } = string.Empty;
        public string CorpusId { get; init; } = CorpusRegistry.CanonicalCorpusId;
        public string Title { get; init; } = string.Empty;
        public string Author { get; init; } = "His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda";
        public string Abbreviation { get; init; } = string.Empty;
        public string Category { get; init; } = "Scripture";
        public int CanonicalOrder { get; init; }
        public int RecordCount { get; set; }
        public bool IsAvailable { get; set; } = true;
    }

    /// <summary>
    /// Registry service for resolving book metadata, titles, and scriptural order.
    /// Decouples hardcoded dictionary lookups from SqliteCorpusRepository.
    /// </summary>
    public interface IBookRegistry
    {
        /// <summary>
        /// Initializes the registry by inspecting available books in the active corpus database.
        /// </summary>
        Task InitializeFromDatabaseAsync(string dbPath);

        /// <summary>
        /// Returns all books registered in the active library, ordered canonically.
        /// </summary>
        IReadOnlyList<BookDescriptor> GetAllBooks(string? corpusId = null);

        /// <summary>
        /// Returns the book descriptor for the specified BookKey, or null if not found.
        /// </summary>
        BookDescriptor? GetBook(string bookKey);

        /// <summary>
        /// Returns the friendly title for a BookKey, falling back gracefully to the key itself if unmapped.
        /// </summary>
        string GetBookTitle(string bookKey);

        /// <summary>
        /// Returns the canonical scriptural reading order of BookKeys.
        /// </summary>
        IReadOnlyList<string> GetCanonicalBookOrder();

        /// <summary>
        /// Returns true if the specified BookKey is available in the registered corpus.
        /// </summary>
        bool IsBookAvailable(string bookKey);
    }
}
