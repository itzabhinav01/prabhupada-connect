using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VedaBaseModern.Core.Registry
{
    /// <summary>
    /// Health and operational status of a registered corpus.
    /// </summary>
    public enum CorpusStatus
    {
        /// <summary>Corpus file is verified, schema-valid, and active for reading and search.</summary>
        Active,
        /// <summary>Corpus database file was not found at any probe location.</summary>
        Missing,
        /// <summary>Corpus file exists but failed integrity verification (hash mismatch or file corruption).</summary>
        Corrupt,
        /// <summary>Corpus file exists and opens as SQLite, but lacks required tables/columns or schema version.</summary>
        Incompatible
    }

    /// <summary>
    /// Descriptive metadata and runtime binding for a registered scripture corpus.
    /// Supports the primary canonical Prabhupāda corpus and future optional supplementary corpora.
    /// </summary>
    public class CorpusDescriptor
    {
        /// <summary>Stable unique identifier for the corpus (e.g. "prabhupada-canonical").</summary>
        public string CorpusId { get; init; } = "prabhupada-canonical";

        /// <summary>Human-readable display name of the corpus.</summary>
        public string Name { get; set; } = "Śrīla Prabhupāda Canonical Library";

        /// <summary>Semantic or release version of this corpus (e.g. "10.2").</summary>
        public string Version { get; set; } = "10.2";

        /// <summary>Internal schema version of the corpus database.</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Resolved absolute filesystem path to the corpus SQLite database file.</summary>
        public string DatabasePath { get; set; } = string.Empty;

        /// <summary>Approved SHA-256 hash string for integrity validation of this release.</summary>
        public string ExpectedSha256 { get; set; } = "4443C98E9DA25B5A561CFD54A3F72841154A313FD4E53415A80A07CA0AF6B7E5";

        /// <summary>Whether the corpus is strictly read-only from the application.</summary>
        public bool IsReadOnly { get; init; } = true;

        /// <summary>Current operational status of this corpus.</summary>
        public CorpusStatus Status { get; set; } = CorpusStatus.Missing;

        /// <summary>Descriptive error or diagnostic message when Status != Active.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Total number of distinct books present in this corpus.</summary>
        public int BooksCount { get; set; }

        /// <summary>Total number of scripture records present in this corpus.</summary>
        public int RecordsCount { get; set; }

        /// <summary>Probe paths that were searched when locating this corpus.</summary>
        public List<string> SearchedPaths { get; } = new();

        /// <summary>True if the corpus is ready for read-only scripture access.</summary>
        public bool IsAvailable => Status == CorpusStatus.Active && !string.IsNullOrEmpty(DatabasePath);
    }

    /// <summary>
    /// Service for discovering, validating, and registering scripture corpora.
    /// Decouples the application from hardcoded developer machine paths.
    /// </summary>
    public interface ICorpusRegistry
    {
        /// <summary>
        /// Returns the canonical Śrīla Prabhupāda corpus descriptor.
        /// </summary>
        CorpusDescriptor GetCanonicalCorpus();

        /// <summary>
        /// Probes standard filesystem locations, discovers the canonical corpus,
        /// inspects schema validity, and verifies integrity.
        /// </summary>
        /// <param name="customCorpusPath">Optional explicit path to override default probe locations.</param>
        /// <param name="forceVerifyHash">If true, recomputes and validates the full SHA-256 hash immediately.</param>
        Task<CorpusDescriptor> InitializeAsync(string? customCorpusPath = null, bool forceVerifyHash = false);

        /// <summary>
        /// Explicitly verifies the SHA-256 content hash of the specified corpus against its approved release hash.
        /// </summary>
        Task<bool> VerifyCorpusIntegrityAsync(string corpusId);

        /// <summary>
        /// Returns the list of standard filesystem probe paths in priority order.
        /// </summary>
        IReadOnlyList<string> GetProbePaths(string? customCorpusPath = null);

        /// <summary>
        /// Registers a supplementary/optional user corpus.
        /// </summary>
        Task<CorpusDescriptor> RegisterSupplementaryCorpusAsync(string corpusId, string databasePath, string name);

        /// <summary>
        /// Returns all currently registered corpora.
        /// </summary>
        IReadOnlyList<CorpusDescriptor> GetAllCorpora();
    }
}
