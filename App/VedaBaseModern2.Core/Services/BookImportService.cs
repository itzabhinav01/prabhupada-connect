using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Registry;

namespace VedaBaseModern.Core.Services
{
    public class BookImportRecord
    {
        public string? RecordKey { get; set; }
        public string? Reference { get; set; }
        public int Sequence { get; set; }
        public string? ParentKey { get; set; }
        public string? RecordType { get; set; } = "Verse";
        public string? ReferenceStatus { get; set; } = "Valid";
        public string? Title { get; set; }
        public string? Devanagari { get; set; }
        public string? Transliteration { get; set; }
        public string? Synonyms { get; set; }
        public string? Translation { get; set; }
        public string? Purports { get; set; }
    }

    public class BookImportPayload
    {
        public string BookKey { get; set; } = string.Empty;
        public string? Abbreviation { get; set; }
        public string? Edition { get; set; } = "Standard";
        public string Title { get; set; } = string.Empty;
        public string? Author { get; set; } = "His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda";
        public string? Category { get; set; } = "Other Books";
        public List<BookImportRecord> Records { get; set; } = new();
    }

    public class BookImportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string BookKey { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public int RecordsImported { get; set; }
    }

    public class BookImportService
    {
        private readonly string _dbPath;
        private readonly IBookRegistry _bookRegistry;

        public BookImportService(string dbPath, IBookRegistry bookRegistry)
        {
            _dbPath = dbPath;
            _bookRegistry = bookRegistry;
        }

        public async Task<BookImportResult> ImportBookFromFileAsync(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = $"Selected JSON file does not exist: {jsonFilePath}"
                };
            }

            try
            {
                string jsonContent = await File.ReadAllTextAsync(jsonFilePath);
                return await ImportBookFromJsonStringAsync(jsonContent);
            }
            catch (Exception ex)
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = $"Failed to read file: {ex.Message}"
                };
            }
        }

        public async Task<BookImportResult> ImportBookFromJsonStringAsync(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = "JSON content is empty."
                };
            }

            BookImportPayload? payload;
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                payload = JsonSerializer.Deserialize<BookImportPayload>(jsonContent, options);
            }
            catch (Exception ex)
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = $"Invalid JSON syntax: {ex.Message}"
                };
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.BookKey) || string.IsNullOrWhiteSpace(payload.Title))
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = "Book payload is missing mandatory fields: 'BookKey' and 'Title' are required."
                };
            }

            if (payload.Records == null || payload.Records.Count == 0)
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = $"No records found in payload for book: {payload.Title} ({payload.BookKey})."
                };
            }

            string cleanKey = payload.BookKey.Trim().ToUpperInvariant();
            string cleanTitle = payload.Title.Trim();
            string cleanAbbr = string.IsNullOrWhiteSpace(payload.Abbreviation) ? cleanKey : payload.Abbreviation.Trim();
            string cleanCategory = string.IsNullOrWhiteSpace(payload.Category) ? "Other Books" : payload.Category.Trim();
            string cleanAuthor = string.IsNullOrWhiteSpace(payload.Author) ? "His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda" : payload.Author.Trim();

            if (!File.Exists(_dbPath))
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = $"Corpus database not found at '{_dbPath}'."
                };
            }

            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                await conn.OpenAsync();

                using var tx = conn.BeginTransaction();

                // 1. Get max CanonicalOrder and max Sequence
                int maxOrder = 50;
                using (var orderCmd = conn.CreateCommand())
                {
                    orderCmd.Transaction = tx;
                    orderCmd.CommandText = "SELECT COALESCE(MAX(CanonicalOrder), 0) FROM Books;";
                    var val = await orderCmd.ExecuteScalarAsync();
                    if (val != null && int.TryParse(val.ToString(), out int o))
                        maxOrder = o + 1;
                }

                int baseSequence = 50000;
                using (var seqCmd = conn.CreateCommand())
                {
                    seqCmd.Transaction = tx;
                    seqCmd.CommandText = "SELECT COALESCE(MAX(Sequence), 0) FROM Records;";
                    var val = await seqCmd.ExecuteScalarAsync();
                    if (val != null && int.TryParse(val.ToString(), out int s))
                        baseSequence = s + 1;
                }

                // 2. Insert or replace Books entry
                using (var bookCmd = conn.CreateCommand())
                {
                    bookCmd.Transaction = tx;
                    bookCmd.CommandText = @"
                        INSERT INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords)
                        VALUES (@BookKey, @Abbr, @Edition, @Title, @Cat, 'prabhupada', @Author, @Order, @Total)
                        ON CONFLICT(BookKey) DO UPDATE SET
                            Title = excluded.Title,
                            Abbreviation = excluded.Abbreviation,
                            Category = excluded.Category,
                            Author = excluded.Author,
                            TotalRecords = excluded.TotalRecords;";

                    bookCmd.Parameters.AddWithValue("@BookKey", cleanKey);
                    bookCmd.Parameters.AddWithValue("@Abbr", cleanAbbr);
                    bookCmd.Parameters.AddWithValue("@Edition", payload.Edition ?? "Standard");
                    bookCmd.Parameters.AddWithValue("@Title", cleanTitle);
                    bookCmd.Parameters.AddWithValue("@Cat", cleanCategory);
                    bookCmd.Parameters.AddWithValue("@Author", cleanAuthor);
                    bookCmd.Parameters.AddWithValue("@Order", maxOrder);
                    bookCmd.Parameters.AddWithValue("@Total", payload.Records.Count);
                    await bookCmd.ExecuteNonQueryAsync();
                }

                // 3. Insert or replace Records (the trigger trg_records_fts_insert automatically indexes into RecordsFts)
                int currentSeq = baseSequence;
                foreach (var rec in payload.Records)
                {
                    string recKey = string.IsNullOrWhiteSpace(rec.RecordKey)
                        ? $"{cleanKey}-{currentSeq}"
                        : rec.RecordKey.Trim();

                    string reference = string.IsNullOrWhiteSpace(rec.Reference)
                        ? recKey
                        : rec.Reference.Trim();

                    using var recCmd = conn.CreateCommand();
                    recCmd.Transaction = tx;
                    recCmd.CommandText = @"
                        INSERT OR REPLACE INTO Records 
                        (RecordKey, BookKey, Sequence, ParentKey, RecordType, Reference, ReferenceStatus, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
                        VALUES (@RecordKey, @BookKey, @Sequence, @ParentKey, @RecordType, @Reference, @RefStatus, @Title, @Devanagari, @Transliteration, @Synonyms, @Translation, @Purports);";

                    recCmd.Parameters.AddWithValue("@RecordKey", recKey);
                    recCmd.Parameters.AddWithValue("@BookKey", cleanKey);
                    recCmd.Parameters.AddWithValue("@Sequence", rec.Sequence > 0 ? rec.Sequence : currentSeq++);
                    recCmd.Parameters.AddWithValue("@ParentKey", (object?)rec.ParentKey ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@RecordType", string.IsNullOrWhiteSpace(rec.RecordType) ? "Verse" : rec.RecordType);
                    recCmd.Parameters.AddWithValue("@Reference", reference);
                    recCmd.Parameters.AddWithValue("@RefStatus", string.IsNullOrWhiteSpace(rec.ReferenceStatus) ? "Valid" : rec.ReferenceStatus);
                    recCmd.Parameters.AddWithValue("@Title", (object?)rec.Title ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Devanagari", (object?)rec.Devanagari ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Transliteration", (object?)rec.Transliteration ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Synonyms", (object?)rec.Synonyms ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Translation", (object?)rec.Translation ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Purports", (object?)rec.Purports ?? DBNull.Value);

                    await recCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                // 4. Update the live BookRegistry
                try
                {
                    await _bookRegistry.InitializeFromDatabaseAsync(_dbPath);
                }
                catch { }

                return new BookImportResult
                {
                    Success = true,
                    BookKey = cleanKey,
                    BookTitle = cleanTitle,
                    RecordsImported = payload.Records.Count,
                    Message = $"Successfully imported '{cleanTitle}' ({cleanKey}) with {payload.Records.Count} records. The book is now available in your Library and Full-Text Search."
                };
            }
            catch (Exception ex)
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = $"Database error during book import: {ex.Message}"
                };
            }
        }

        private static readonly HashSet<string> ProtectedCanonicalKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "BG", "SB", "DI", "MADHYA", "ANTYA", "CC", "NOD", "TLC", "KB", "ISO", "NOI",
            "TLK", "TQK", "BS", "MM", "NBS", "BB", "DS", "BBD", "POY", "RV",
            "EKC", "KCTYS", "MOG", "LOB", "PQPA", "SSR", "JSD", "LCFL", "CB", "CAT",
            "OWK", "SFL", "TT", "EJ", "SC", "DWT", "POP", "QFE",
            "RTW", "LON", "MG", "ROP", "GG", "SPL"
        };

        public bool IsBookProtected(string bookKey) => ProtectedCanonicalKeys.Contains(bookKey);

        public async Task<BookImportResult> ImportPdfBookAsync(string pdfSourcePath, string title, string? author, string? category)
        {
            if (!File.Exists(pdfSourcePath))
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = $"Selected PDF file does not exist: {pdfSourcePath}"
                };
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = Path.GetFileNameWithoutExtension(pdfSourcePath);
            }

            string cleanTitle = title.Trim();
            string cleanAuthor = string.IsNullOrWhiteSpace(author) ? "Vaiṣṇava Ācārya" : author.Trim();
            string cleanCategory = string.IsNullOrWhiteSpace(category) ? "Works by Other Ācāryas & Authors" : category.Trim();

            string rawKey = "PDF_" + System.Text.RegularExpressions.Regex.Replace(cleanTitle.ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');
            if (rawKey.Length > 24) rawKey = rawKey.Substring(0, 24);
            string cleanKey = $"{rawKey}_{DateTime.UtcNow.Ticks % 10000}";

            try
            {
                string destFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VedaBaseModern",
                    "PdfBooks");
                Directory.CreateDirectory(destFolder);

                string destPath = Path.Combine(destFolder, $"{cleanKey}.pdf");
                File.Copy(pdfSourcePath, destPath, true);

                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                await conn.OpenAsync();

                using var tx = conn.BeginTransaction();

                int maxOrder = 90;
                using (var orderCmd = conn.CreateCommand())
                {
                    orderCmd.Transaction = tx;
                    orderCmd.CommandText = "SELECT COALESCE(MAX(CanonicalOrder), 0) FROM Books;";
                    var val = await orderCmd.ExecuteScalarAsync();
                    if (val != null && int.TryParse(val.ToString(), out int o))
                        maxOrder = o + 1;
                }

                int baseSequence = 60000;
                using (var seqCmd = conn.CreateCommand())
                {
                    seqCmd.Transaction = tx;
                    seqCmd.CommandText = "SELECT COALESCE(MAX(Sequence), 0) FROM Records;";
                    var val = await seqCmd.ExecuteScalarAsync();
                    if (val != null && int.TryParse(val.ToString(), out int s))
                        baseSequence = s + 1;
                }

                // Insert into Books
                using (var bookCmd = conn.CreateCommand())
                {
                    bookCmd.Transaction = tx;
                    bookCmd.CommandText = @"
                        INSERT INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords, IsPdf, PdfPath)
                        VALUES (@BookKey, 'PDF', 'PDF Document', @Title, @Cat, 'imported', @Author, @Order, 1, 1, @PdfPath);";

                    bookCmd.Parameters.AddWithValue("@BookKey", cleanKey);
                    bookCmd.Parameters.AddWithValue("@Title", cleanTitle);
                    bookCmd.Parameters.AddWithValue("@Cat", cleanCategory);
                    bookCmd.Parameters.AddWithValue("@Author", cleanAuthor);
                    bookCmd.Parameters.AddWithValue("@Order", maxOrder);
                    bookCmd.Parameters.AddWithValue("@PdfPath", destPath);
                    await bookCmd.ExecuteNonQueryAsync();
                }

                // Insert 1 record into Records
                using (var recCmd = conn.CreateCommand())
                {
                    recCmd.Transaction = tx;
                    recCmd.CommandText = @"
                        INSERT INTO Records 
                        (RecordKey, BookKey, Sequence, RecordType, Reference, ReferenceStatus, Title, Translation, Purports)
                        VALUES (@RecordKey, @BookKey, @Sequence, 'Document', @Reference, 'Valid', @Title, @Translation, @Purports);";

                    recCmd.Parameters.AddWithValue("@RecordKey", $"{cleanKey}-1");
                    recCmd.Parameters.AddWithValue("@BookKey", cleanKey);
                    recCmd.Parameters.AddWithValue("@Sequence", baseSequence);
                    recCmd.Parameters.AddWithValue("@Reference", $"{cleanTitle} (PDF)");
                    recCmd.Parameters.AddWithValue("@Title", cleanTitle);
                    recCmd.Parameters.AddWithValue("@Translation", $"PDF Book by {cleanAuthor}");
                    recCmd.Parameters.AddWithValue("@Purports", $"[PDF Document] {cleanTitle} by {cleanAuthor}\nFile: {destPath}");
                    await recCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                try
                {
                    await _bookRegistry.InitializeFromDatabaseAsync(_dbPath);
                }
                catch { }

                return new BookImportResult
                {
                    Success = true,
                    BookKey = cleanKey,
                    BookTitle = cleanTitle,
                    RecordsImported = 1,
                    Message = $"Successfully imported PDF '{cleanTitle}' by {cleanAuthor}. It is now directly readable in your Library."
                };
            }
            catch (Exception ex)
            {
                return new BookImportResult
                {
                    Success = false,
                    Message = $"Failed to import PDF book: {ex.Message}"
                };
            }
        }

        public async Task<bool> DeleteBookAsync(string bookKey)
        {
            if (string.IsNullOrWhiteSpace(bookKey) || IsBookProtected(bookKey))
            {
                return false;
            }

            try
            {
                string cleanKey = bookKey.Trim();
                string? pdfPath = null;

                using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await conn.OpenAsync();

                    // Check if PDF and get path
                    using (var checkCmd = conn.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT PdfPath FROM Books WHERE BookKey = @BookKey;";
                        checkCmd.Parameters.AddWithValue("@BookKey", cleanKey);
                        var val = await checkCmd.ExecuteScalarAsync();
                        if (val != null && val != DBNull.Value)
                        {
                            pdfPath = val.ToString();
                        }
                    }

                    using var tx = conn.BeginTransaction();

                    using (var delRecCmd = conn.CreateCommand())
                    {
                        delRecCmd.Transaction = tx;
                        delRecCmd.CommandText = "DELETE FROM Records WHERE BookKey = @BookKey;";
                        delRecCmd.Parameters.AddWithValue("@BookKey", cleanKey);
                        await delRecCmd.ExecuteNonQueryAsync();
                    }

                    using (var delBookCmd = conn.CreateCommand())
                    {
                        delBookCmd.Transaction = tx;
                        delBookCmd.CommandText = "DELETE FROM Books WHERE BookKey = @BookKey;";
                        delBookCmd.Parameters.AddWithValue("@BookKey", cleanKey);
                        await delBookCmd.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                }

                // Delete PDF file if present
                if (!string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath))
                {
                    try { File.Delete(pdfPath); } catch { }
                }

                try
                {
                    await _bookRegistry.InitializeFromDatabaseAsync(_dbPath);
                }
                catch { }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BookImportService] Failed to delete book {bookKey}: {ex}");
                return false;
            }
        }

        public async Task<List<BookNode>> GetImportedBooksAsync()
        {
            var list = new List<BookNode>();
            if (!File.Exists(_dbPath)) return list;

            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT BookKey, Title, Author, Category, IsPdf, PdfPath FROM Books;";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string key = reader.GetString(0);
                    if (IsBookProtected(key)) continue;

                    string title = reader.IsDBNull(1) ? key : reader.GetString(1);
                    string author = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    string category = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    bool isPdf = reader.FieldCount > 4 && !reader.IsDBNull(4) && reader.GetInt32(4) != 0;
                    string? pdfPath = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null;

                    list.Add(new BookNode
                    {
                        BookKey = key,
                        Title = title,
                        Author = author,
                        Category = category,
                        IsPdf = isPdf,
                        PdfPath = pdfPath
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BookImportService] Failed to get imported books: {ex}");
            }

            return list;
        }
    }
}
