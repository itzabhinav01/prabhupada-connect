using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace VedaBaseModern.Core.Registry
{
    /// <summary>
    /// Production implementation of ICorpusRegistry.
    /// Manages multi-path discovery, integrity verification, and runtime registration
    /// of the canonical Śrīla Prabhupāda corpus and optional future supplementary corpora.
    /// </summary>
    public class CorpusRegistry : ICorpusRegistry
    {
        public const string CanonicalCorpusId = "prabhupada-canonical";
        public const string CanonicalCorpusName = "Śrīla Prabhupāda Canonical Library";
        public const string CanonicalCorpusVersion = "10.2";
        public const string ExpectedCanonicalSha256 = "4443C98E9DA25B5A561CFD54A3F72841154A313FD4E53415A80A07CA0AF6B7E5";
        public const string StandardCorpusFileName = "prabhupada_corpus.db";
        public const string V11CorpusFileName = "prabhupada_corpus_v11.db";
        public const string LegacyCorpusFileName = "corpus_v10_2_canonical.db";

        private readonly ConcurrentDictionary<string, CorpusDescriptor> _registeredCorpora = new(StringComparer.OrdinalIgnoreCase);
        private CorpusDescriptor _canonicalCorpus;

        // In-memory cache of verified file hashes: Path -> (LastWriteUtc, FileSize, VerifiedSuccess)
        private static readonly ConcurrentDictionary<string, (DateTime LastWriteUtc, long Length, bool IsValid)> _hashValidationCache = new(StringComparer.OrdinalIgnoreCase);

        public CorpusRegistry()
        {
            _canonicalCorpus = new CorpusDescriptor
            {
                CorpusId = CanonicalCorpusId,
                Name = CanonicalCorpusName,
                Version = CanonicalCorpusVersion,
                ExpectedSha256 = ExpectedCanonicalSha256,
                IsReadOnly = true,
                Status = CorpusStatus.Missing
            };
            _registeredCorpora[CanonicalCorpusId] = _canonicalCorpus;
        }

        public CorpusDescriptor GetCanonicalCorpus() => _canonicalCorpus;

        public IReadOnlyList<CorpusDescriptor> GetAllCorpora()
        {
            return new List<CorpusDescriptor>(_registeredCorpora.Values);
        }

        public IReadOnlyList<string> GetProbePaths(string? customCorpusPath = null)
        {
            var paths = new List<string>();

            if (!string.IsNullOrWhiteSpace(customCorpusPath))
            {
                paths.Add(Path.GetFullPath(customCorpusPath));
                return paths;
            }

            // 1. Packaged application directory (Corpus/prabhupada_corpus.db and variations)
            try
            {
                string baseDir = AppContext.BaseDirectory;
                paths.Add(Path.Combine(baseDir, "Corpus", V11CorpusFileName));
                paths.Add(Path.Combine(baseDir, V11CorpusFileName));
                paths.Add(Path.Combine(baseDir, "Corpus", StandardCorpusFileName));
                paths.Add(Path.Combine(baseDir, "Corpus", LegacyCorpusFileName));
                paths.Add(Path.Combine(baseDir, StandardCorpusFileName));
            }
            catch { }

            // 2. LocalApplicationData directory (%LOCALAPPDATA%\VedaBaseModern\Corpus\prabhupada_corpus_v11.db)
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localApp))
                {
                    paths.Add(Path.Combine(localApp, "VedaBaseModern", "Corpus", V11CorpusFileName));
                    paths.Add(Path.Combine(localApp, "VedaBaseModern", "Corpus", StandardCorpusFileName));
                    paths.Add(Path.Combine(localApp, "VedaBaseModern", "Corpus", LegacyCorpusFileName));
                }
            }
            catch { }

            // 3. CommonApplicationData / ProgramData (%PROGRAMDATA%\VedaBaseModern\Corpus\prabhupada_corpus.db)
            try
            {
                string commonApp = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrEmpty(commonApp))
                {
                    paths.Add(Path.Combine(commonApp, "VedaBaseModern", "Corpus", V11CorpusFileName));
                    paths.Add(Path.Combine(commonApp, "VedaBaseModern", "Corpus", StandardCorpusFileName));
                    paths.Add(Path.Combine(commonApp, "VedaBaseModern", "Corpus", LegacyCorpusFileName));
                }
            }
            catch { }

            // 4. Legacy development fallback (SmokeTest\UnknownResolution\corpus_v10_2_canonical.db)
            paths.Add(@"C:\VedaBaseModern\SmokeTest\UnknownResolution\corpus_v10_2_canonical.db");

            return paths;
        }

        public async Task<CorpusDescriptor> InitializeAsync(string? customCorpusPath = null, bool forceVerifyHash = false)
        {
            var probePaths = GetProbePaths(customCorpusPath);
            _canonicalCorpus.SearchedPaths.Clear();
            _canonicalCorpus.SearchedPaths.AddRange(probePaths);

            string? foundPath = null;
            foreach (var path in probePaths)
            {
                if (File.Exists(path))
                {
                    foundPath = path;
                    break;
                }
            }

            if (foundPath == null)
            {
                _canonicalCorpus.Status = CorpusStatus.Missing;
                _canonicalCorpus.DatabasePath = string.Empty;
                _canonicalCorpus.ErrorMessage = "The canonical Śrīla Prabhupāda corpus database was not found at any standard location. " +
                                                "Please verify application installation or package integrity.";
                return _canonicalCorpus;
            }

            _canonicalCorpus.DatabasePath = foundPath;

            // Inspect and validate SQLite schema
            var schemaResult = await ValidateCorpusSchemaAsync(foundPath);
            if (!schemaResult.IsValid)
            {
                _canonicalCorpus.Status = schemaResult.Status;
                _canonicalCorpus.ErrorMessage = schemaResult.ErrorMessage;
                return _canonicalCorpus;
            }

            _canonicalCorpus.RecordsCount = schemaResult.RecordsCount;
            _canonicalCorpus.BooksCount = schemaResult.BooksCount;

            // Determine version from database metadata
            string detectedVersion = "10.2";
            try
            {
                using var conn = new SqliteConnection($"Data Source={foundPath};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM CorpusMetadata WHERE Key = 'CorpusVersion';";
                var val = cmd.ExecuteScalar();
                if (val != null && val != DBNull.Value)
                {
                    detectedVersion = val.ToString() ?? "10.2";
                }
            }
            catch { }

            bool isV11 = detectedVersion.StartsWith("11", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetFileName(foundPath).Contains("v11", StringComparison.OrdinalIgnoreCase);

            if (isV11)
            {
                _canonicalCorpus.Version = detectedVersion;
                _canonicalCorpus.Name = "Śrīla Prabhupāda Canonical Library (v11.0)";

                // Check companion .sha256 signature if present
                string shaFile = foundPath + ".sha256";
                if (File.Exists(shaFile))
                {
                    string expectedSha = (await File.ReadAllTextAsync(shaFile)).Trim();
                    _canonicalCorpus.ExpectedSha256 = expectedSha;
                    bool isHashValid = await ValidateOrCheckCachedHashAsync(foundPath, expectedSha, forceVerifyHash);
                    if (!isHashValid)
                    {
                        _canonicalCorpus.Status = CorpusStatus.Corrupt;
                        _canonicalCorpus.ErrorMessage = $"The canonical corpus database failed SHA-256 integrity verification. " +
                                                        $"The file at '{foundPath}' does not match the approved canonical checksum.";
                        return _canonicalCorpus;
                    }
                }
            }
            else
            {
                // Verify SHA-256 integrity against baseline v10.2
                bool isHashValid = await ValidateOrCheckCachedHashAsync(foundPath, _canonicalCorpus.ExpectedSha256, forceVerifyHash);
                if (!isHashValid)
                {
                    _canonicalCorpus.Status = CorpusStatus.Corrupt;
                    _canonicalCorpus.ErrorMessage = $"The canonical corpus database failed SHA-256 integrity verification. " +
                                                    $"The file at '{foundPath}' does not match the approved canonical checksum.";
                    return _canonicalCorpus;
                }
            }

            _canonicalCorpus.Status = CorpusStatus.Active;
            _canonicalCorpus.ErrorMessage = null;
            _registeredCorpora[CanonicalCorpusId] = _canonicalCorpus;

            return _canonicalCorpus;
        }

        public async Task<bool> VerifyCorpusIntegrityAsync(string corpusId)
        {
            if (!_registeredCorpora.TryGetValue(corpusId, out var descriptor))
                return false;

            if (string.IsNullOrEmpty(descriptor.DatabasePath) || !File.Exists(descriptor.DatabasePath))
                return false;

            return await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(descriptor.DatabasePath);
                    using var sha = SHA256.Create();
                    byte[] hash = sha.ComputeHash(stream);
                    string actualHash = Convert.ToHexString(hash);

                    bool valid = string.Equals(actualHash, descriptor.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
                    
                    var fi = new FileInfo(descriptor.DatabasePath);
                    _hashValidationCache[descriptor.DatabasePath] = (fi.LastWriteTimeUtc, fi.Length, valid);

                    return valid;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<CorpusDescriptor> RegisterSupplementaryCorpusAsync(string corpusId, string databasePath, string name)
        {
            var descriptor = new CorpusDescriptor
            {
                CorpusId = corpusId,
                Name = name,
                DatabasePath = databasePath,
                IsReadOnly = true,
                Status = CorpusStatus.Missing
            };

            if (!File.Exists(databasePath))
            {
                descriptor.Status = CorpusStatus.Missing;
                descriptor.ErrorMessage = $"Supplementary corpus database was not found at '{databasePath}'.";
                _registeredCorpora[corpusId] = descriptor;
                return descriptor;
            }

            var schema = await ValidateCorpusSchemaAsync(databasePath);
            if (!schema.IsValid)
            {
                descriptor.Status = schema.Status;
                descriptor.ErrorMessage = schema.ErrorMessage;
            }
            else
            {
                descriptor.Status = CorpusStatus.Active;
                descriptor.RecordsCount = schema.RecordsCount;
                descriptor.BooksCount = schema.BooksCount;
            }

            _registeredCorpora[corpusId] = descriptor;
            return descriptor;
        }

        private static async Task<(bool IsValid, CorpusStatus Status, string? ErrorMessage, int RecordsCount, int BooksCount)> ValidateCorpusSchemaAsync(string dbPath)
        {
            return await Task.Run<(bool IsValid, CorpusStatus Status, string? ErrorMessage, int RecordsCount, int BooksCount)>(() =>
            {
                try
                {
                    // Check file signature (SQLite format 3)
                    using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length < 100)
                        {
                            return (false, CorpusStatus.Incompatible, "File is smaller than minimal SQLite header.", 0, 0);
                        }

                        byte[] header = new byte[16];
                        int bytesRead = fs.Read(header, 0, 16);
                        string magic = System.Text.Encoding.ASCII.GetString(header, 0, bytesRead);
                        if (!magic.StartsWith("SQLite format 3\0", StringComparison.Ordinal))
                        {
                            return (false, CorpusStatus.Incompatible, "File is not a valid SQLite database (header mismatch).", 0, 0);
                        }
                    }

                    using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                    conn.Open();

                    // Check required tables
                    var requiredTables = new[] { "Records", "Books", "RecordsFts", "CorpusMetadata" };
                    foreach (var table in requiredTables)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type IN ('table', 'view') AND name = $name;";
                        cmd.Parameters.AddWithValue("$name", table);
                        var result = cmd.ExecuteScalar();
                        if (result == null || result == DBNull.Value)
                        {
                            return (false, CorpusStatus.Incompatible, $"Corpus is missing required table '{table}'.", 0, 0);
                        }
                    }

                    // Check required columns on Records
                    var requiredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "RecordKey", "BookKey", "Sequence", "Reference", "Translation", "Purports"
                    };

                    using (var colCmd = conn.CreateCommand())
                    {
                        colCmd.CommandText = "PRAGMA table_info(Records);";
                        using var reader = colCmd.ExecuteReader();
                        while (reader.Read())
                        {
                            string colName = reader.GetString(1);
                            requiredColumns.Remove(colName);
                        }
                    }

                    if (requiredColumns.Count > 0)
                    {
                        return (false, CorpusStatus.Incompatible, $"Records table is missing required columns: {string.Join(", ", requiredColumns)}.", 0, 0);
                    }

                    // Get counts
                    int recordsCount = 0;
                    using (var countCmd = conn.CreateCommand())
                    {
                        countCmd.CommandText = "SELECT COUNT(*) FROM Records;";
                        recordsCount = Convert.ToInt32(countCmd.ExecuteScalar());
                    }

                    int booksCount = 0;
                    using (var bCountCmd = conn.CreateCommand())
                    {
                        bCountCmd.CommandText = "SELECT COUNT(DISTINCT BookKey) FROM Records;";
                        booksCount = Convert.ToInt32(bCountCmd.ExecuteScalar());
                    }

                    if (recordsCount <= 0)
                    {
                        return (false, CorpusStatus.Incompatible, "Corpus contains 0 scripture records.", 0, 0);
                    }

                    return (true, CorpusStatus.Active, null, recordsCount, booksCount);
                }
                catch (Exception ex)
                {
                    return (false, CorpusStatus.Incompatible, $"Failed to open database: {ex.Message}", 0, 0);
                }
            });
        }

        private async Task<bool> ValidateOrCheckCachedHashAsync(string dbPath, string expectedHash, bool forceVerify)
        {
            var fi = new FileInfo(dbPath);
            if (!forceVerify && _hashValidationCache.TryGetValue(dbPath, out var cached))
            {
                if (cached.LastWriteUtc == fi.LastWriteTimeUtc && cached.Length == fi.Length)
                {
                    return cached.IsValid;
                }
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(dbPath);
                    using var sha = SHA256.Create();
                    byte[] hash = sha.ComputeHash(stream);
                    string actualHash = Convert.ToHexString(hash);

                    bool isValid = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
                    _hashValidationCache[dbPath] = (fi.LastWriteTimeUtc, fi.Length, isValid);
                    return isValid;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
