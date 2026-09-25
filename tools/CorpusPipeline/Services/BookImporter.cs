using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.CorpusPipeline.Services
{
    public class BookImportPayload
    {
        public string BookKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = "A.C. Bhaktivedanta Swami Prabhupada";
        public string Language { get; set; } = "en";
        public List<CorpusRecord> Records { get; set; } = new();
    }

    public class BookImporter
    {
        private readonly string _dbPath;

        public BookImporter(string dbPath)
        {
            _dbPath = dbPath;
        }

        public async Task<int> ImportBookFromJsonAsync(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");
            }

            string json = await File.ReadAllTextAsync(jsonFilePath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var payload = JsonSerializer.Deserialize<BookImportPayload>(json, options);

            if (payload == null || string.IsNullOrWhiteSpace(payload.BookKey) || string.IsNullOrWhiteSpace(payload.Title))
            {
                throw new InvalidOperationException("Invalid book payload. BookKey and Title are mandatory.");
            }

            if (payload.Records == null || payload.Records.Count == 0)
            {
                throw new InvalidOperationException($"No records found in payload for book: {payload.BookKey}");
            }

            Console.WriteLine($"[Pipeline] Importing '{payload.Title}' ({payload.BookKey}) with {payload.Records.Count} records...");

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // 1. Get max sequence in corpus
                int baseSequence = 10000;
                using (var maxSeqCmd = connection.CreateCommand())
                {
                    maxSeqCmd.Transaction = transaction;
                    maxSeqCmd.CommandText = "SELECT COALESCE(MAX(Sequence), 0) FROM Records;";
                    var maxVal = await maxSeqCmd.ExecuteScalarAsync();
                    if (maxVal != null && int.TryParse(maxVal.ToString(), out int parsedMax))
                    {
                        baseSequence = parsedMax + 1;
                    }
                }

                // 2. Insert or update Books table
                using (var bookCmd = connection.CreateCommand())
                {
                    bookCmd.Transaction = transaction;
                    bookCmd.CommandText = @"
                        INSERT INTO Books (BookKey, Title, CanonicalOrder)
                        VALUES (@BookKey, @Title, @CanonicalOrder)
                        ON CONFLICT(BookKey) DO UPDATE SET
                            Title = excluded.Title,
                            CanonicalOrder = excluded.CanonicalOrder;";
                    bookCmd.Parameters.AddWithValue("@BookKey", payload.BookKey);
                    bookCmd.Parameters.AddWithValue("@Title", payload.Title);
                    bookCmd.Parameters.AddWithValue("@CanonicalOrder", baseSequence);
                    await bookCmd.ExecuteNonQueryAsync();
                }

                // 3. Batch insert Records
                int currentSeq = baseSequence;
                foreach (var rec in payload.Records)
                {
                    using var recCmd = connection.CreateCommand();
                    recCmd.Transaction = transaction;
                    recCmd.CommandText = @"
                        INSERT OR REPLACE INTO Records 
                        (RecordKey, BookKey, Reference, Sequence, RecordType, ReferenceStatus, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
                        VALUES (@RecordKey, @BookKey, @Reference, @Sequence, 'Verse', 'Valid', @Title, @Devanagari, @Transliteration, @Synonyms, @Translation, @Purports);";

                    recCmd.Parameters.AddWithValue("@RecordKey", rec.RecordKey ?? $"{payload.BookKey}-{currentSeq}");
                    recCmd.Parameters.AddWithValue("@BookKey", payload.BookKey);
                    recCmd.Parameters.AddWithValue("@Reference", rec.Reference ?? rec.RecordKey ?? "");
                    recCmd.Parameters.AddWithValue("@Sequence", rec.Sequence > 0 ? rec.Sequence : currentSeq++);
                    recCmd.Parameters.AddWithValue("@Title", (object?)rec.Title ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Devanagari", (object?)rec.Devanagari ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Transliteration", (object?)rec.Transliteration ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Synonyms", (object?)rec.Synonyms ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Translation", (object?)rec.Translation ?? (object?)rec.CleanTranslation ?? DBNull.Value);
                    recCmd.Parameters.AddWithValue("@Purports", (object?)rec.Purports ?? DBNull.Value);
                    await recCmd.ExecuteNonQueryAsync();

                    // 4. Update SearchIndex FTS5 if it exists
                    try
                    {
                        using var ftsCmd = connection.CreateCommand();
                        ftsCmd.Transaction = transaction;
                        ftsCmd.CommandText = @"
                            INSERT OR REPLACE INTO SearchIndex (RecordKey, BookKey, Reference, Devanagari, Transliteration, Synonyms, Translation, Purports)
                            VALUES (@RecordKey, @BookKey, @Reference, @Devanagari, @Transliteration, @Synonyms, @Translation, @Purports);";
                        ftsCmd.Parameters.AddWithValue("@RecordKey", rec.RecordKey ?? $"{payload.BookKey}-{currentSeq}");
                        ftsCmd.Parameters.AddWithValue("@BookKey", payload.BookKey);
                        ftsCmd.Parameters.AddWithValue("@Reference", rec.Reference ?? rec.RecordKey ?? "");
                        ftsCmd.Parameters.AddWithValue("@Devanagari", (object?)rec.Devanagari ?? DBNull.Value);
                        ftsCmd.Parameters.AddWithValue("@Transliteration", (object?)rec.Transliteration ?? DBNull.Value);
                        ftsCmd.Parameters.AddWithValue("@Synonyms", (object?)rec.Synonyms ?? DBNull.Value);
                        ftsCmd.Parameters.AddWithValue("@Translation", (object?)rec.Translation ?? (object?)rec.CleanTranslation ?? DBNull.Value);
                        ftsCmd.Parameters.AddWithValue("@Purports", (object?)rec.Purports ?? DBNull.Value);
                        await ftsCmd.ExecuteNonQueryAsync();
                    }
                    catch { /* FTS table might be updated in separate step */ }
                }

                await transaction.CommitAsync();
                Console.WriteLine($"[Pipeline] Successfully imported {payload.Records.Count} verses for book '{payload.Title}'.");
                return payload.Records.Count;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
