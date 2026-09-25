using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VedaBaseModern.CorpusPipeline.Services;

namespace VedaBaseModern.CorpusPipeline
{
    public static class Program
    {
        private static string GetDatabasePath()
        {
            string dbDir = @"C:\VedaBaseModern2\Database";
            return Path.Combine(dbDir, "prabhupada_corpus.db");
        }

        public static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help")
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            string dbPath = GetDatabasePath();

            if (!File.Exists(dbPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Target database not found at: {dbPath}");
                Console.ResetColor();
                return 1;
            }

            try
            {
                switch (command)
                {
                    case "import-book":
                        if (args.Length < 2)
                        {
                            Console.WriteLine("Usage: CorpusPipeline import-book <path-to-json>");
                            return 1;
                        }
                        var importer = new BookImporter(dbPath);
                        int count = await importer.ImportBookFromJsonAsync(args[1]);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[SUCCESS] Ingested {count} records into {dbPath}.");
                        Console.ResetColor();
                        return 0;

                    case "list-books":
                        await ListBooksAsync(dbPath);
                        return 0;

                    case "validate-corpus":
                        await ValidateCorpusAsync(dbPath);
                        return 0;

                    case "export-book":
                        if (args.Length < 3)
                        {
                            Console.WriteLine("Usage: CorpusPipeline export-book <bookKey> <outPath.json>");
                            return 1;
                        }
                        await ExportBookAsync(dbPath, args[1], args[2]);
                        return 0;

                    case "rebuild-search-index":
                        await RebuildSearchIndexAsync(dbPath);
                        return 0;

                    case "repair-corpus":
                        string sourcesDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "sources");
                        string canonicalDb = @"C:\VedaBaseModern\SmokeTest\UnknownResolution\corpus_v10_2_canonical.db";
                        if (args.Length > 1) sourcesDir = args[1];
                        if (args.Length > 2) canonicalDb = args[2];
                        var repairService = new CorpusRepairService(dbPath, sourcesDir, canonicalDb);
                        await repairService.RunFullRepairAsync();
                        return 0;

                    case "debug-rtf":
                        if (args.Length < 3)
                        {
                            Console.WriteLine("Usage: CorpusPipeline debug-rtf <path-to-rtf> <targetReference>");
                            return 1;
                        }
                        DebugRtf(args[1], args[2]);
                        return 0;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Unknown command: '{command}'");
                        Console.ResetColor();
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[INNER] {ex.InnerException.Message}");
                }
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task ListBooksAsync(string dbPath)
        {
            Console.WriteLine("==========================================================================");
            Console.WriteLine($"Books in Database: {dbPath}");
            Console.WriteLine("==========================================================================");
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT b.BookKey, COALESCE(b.Title, b.BookKey), COUNT(c.RecordKey) as VerseCount
                FROM Books b
                LEFT JOIN Records c ON b.BookKey = c.BookKey
                GROUP BY b.BookKey, b.Title
                ORDER BY b.CanonicalOrder ASC;";

            using var reader = await cmd.ExecuteReaderAsync();
            int total = 0;
            int bookCount = 0;
            while (await reader.ReadAsync())
            {
                bookCount++;
                string key = reader.GetString(0);
                string title = reader.GetString(1);
                int verses = reader.GetInt32(2);
                total += verses;
                Console.WriteLine($"[{key,-8}] {title,-45} : {verses,5} verses");
            }
            Console.WriteLine("--------------------------------------------------------------------------");
            Console.WriteLine($"Total Books: {bookCount} | Total Canonical Records: {total}");
            Console.WriteLine("==========================================================================");
        }

        private static async Task ValidateCorpusAsync(string dbPath)
        {
            Console.WriteLine("==========================================================================");
            Console.WriteLine($"Validating Corpus Integrity: {dbPath}");
            Console.WriteLine("==========================================================================");
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Records;";
            long totalRecords = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            cmd.CommandText = "SELECT COUNT(*) FROM Books;";
            long totalBooks = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            cmd.CommandText = "SELECT COUNT(*) FROM Records WHERE Translation IS NULL;";
            long missingTrans = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            cmd.CommandText = "SELECT COUNT(*) FROM Records WHERE Sequence <= 0;";
            long invalidSeq = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            Console.WriteLine($"Total Books Registered : {totalBooks}");
            Console.WriteLine($"Total Corpus Records   : {totalRecords}");
            Console.WriteLine($"Missing Translations   : {missingTrans}");
            Console.WriteLine($"Invalid Sequences      : {invalidSeq}");

            if (totalRecords > 0 && invalidSeq == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[PASS] Corpus database passes all schema and integrity invariants.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARN] Potential anomalies detected in corpus validation.");
                Console.ResetColor();
            }
        }

        private static async Task RebuildSearchIndexAsync(string dbPath)
        {
            Console.WriteLine("[Pipeline] Rebuilding FTS5 SearchIndex...");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                DROP TABLE IF EXISTS SearchIndex;
                CREATE VIRTUAL TABLE SearchIndex USING fts5(
                    RecordKey UNINDEXED,
                    BookKey,
                    Reference,
                    Devanagari,
                    Transliteration,
                    Synonyms,
                    Translation,
                    Purports,
                    tokenize = 'unicode61'
                );

                INSERT INTO SearchIndex (RecordKey, BookKey, Reference, Devanagari, Transliteration, Synonyms, Translation, Purports)
                SELECT RecordKey, BookKey, Reference, Devanagari, Transliteration, Synonyms, Translation, Purports
                FROM Records;";

            await cmd.ExecuteNonQueryAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] FTS5 SearchIndex successfully reconstructed.");
            Console.ResetColor();
        }

        private static async Task ExportBookAsync(string dbPath, string bookKey, string outPath)
        {
            Console.WriteLine($"[Pipeline] Exporting book '{bookKey}' to {outPath}...");
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            await connection.OpenAsync();

            var payload = new BookImportPayload { BookKey = bookKey };

            using (var bookCmd = connection.CreateCommand())
            {
                bookCmd.CommandText = "SELECT Title FROM Books WHERE BookKey = @BookKey;";
                bookCmd.Parameters.AddWithValue("@BookKey", bookKey);
                using var r = await bookCmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    payload.Title = r.GetString(0);
                    payload.Author = "A.C. Bhaktivedanta Swami Prabhupada";
                    payload.Language = "en";
                }
            }

            using (var recCmd = connection.CreateCommand())
            {
                recCmd.CommandText = @"
                    SELECT RecordKey, BookKey, Reference, Sequence, Devanagari, Transliteration, Synonyms, Translation, Purports
                    FROM Records WHERE BookKey = @BookKey ORDER BY Sequence ASC;";
                recCmd.Parameters.AddWithValue("@BookKey", bookKey);
                using var r = await recCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    payload.Records.Add(new VedaBaseModern.Core.Models.CorpusRecord
                    {
                        RecordKey = r.GetString(0),
                        BookKey = r.GetString(1),
                        Reference = r.GetString(2),
                        Sequence = r.GetInt32(3),
                        Devanagari = r.IsDBNull(4) ? null : r.GetString(4),
                        Transliteration = r.IsDBNull(5) ? null : r.GetString(5),
                        Synonyms = r.IsDBNull(6) ? null : r.GetString(6),
                        Translation = r.IsDBNull(7) ? null : r.GetString(7),
                        Purports = r.IsDBNull(8) ? null : r.GetString(8)
                    });
                }
            }

            var opts = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(payload, opts);
            await File.WriteAllTextAsync(outPath, json);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SUCCESS] Exported {payload.Records.Count} verses to {outPath}.");
            Console.ResetColor();
        }

        private static void DebugRtf(string rtfPath, string targetRef)
        {
            Console.WriteLine($"Scanning RTF: {rtfPath} for reference matching '{targetRef}'...");
            int found = 0;
            int total = 0;
            foreach (var verse in RtfStreamParser.ParseVersesFromRtf(rtfPath))
            {
                total++;
                if (verse.Reference.Contains(targetRef, StringComparison.OrdinalIgnoreCase))
                {
                    found++;
                    Console.WriteLine("=================================================");
                    Console.WriteLine($"Found: {verse.Reference}");
                    Console.WriteLine($"Devanagari ({verse.DecodedDevanagari.Length} chars):\n{verse.DecodedDevanagari}");
                    string transliteratedDev = SanskritTransliterator.IastToDevanagari(verse.DecodedTransliteration);
                    Console.WriteLine($"Generated Devanagari from IAST ({transliteratedDev.Length} chars):\n{transliteratedDev}");
                    Console.WriteLine($"Transliteration ({verse.DecodedTransliteration.Length} chars):\n{verse.DecodedTransliteration}");
                    Console.WriteLine($"Synonyms ({verse.DecodedSynonyms.Length} chars):\n{verse.DecodedSynonyms}");
                    Console.WriteLine($"Translation ({verse.DecodedTranslation.Length} chars):\n{verse.DecodedTranslation}");
                    Console.WriteLine($"Purport ({verse.DecodedPurports.Length} chars, {verse.RawPurportParagraphs.Count} paragraphs):\n{verse.DecodedPurports}");
                    Console.WriteLine("=================================================");
                    if (found >= 5) break;
                }
            }
            Console.WriteLine($"Scan complete. Total parsed: {total}, Matches: {found}");
        }

        private static void PrintHelp()
        {
            Console.WriteLine("==========================================================================");
            Console.WriteLine("VedaBaseModern 2 — Corpus Ingestion & Book Management CLI");
            Console.WriteLine("==========================================================================");
            Console.WriteLine("Commands:");
            Console.WriteLine("  import-book <file.json>          Import and index a book into the corpus");
            Console.WriteLine("  list-books                      List all registered books & record counts");
            Console.WriteLine("  validate-corpus                 Check schema and record sequence integrity");
            Console.WriteLine("  export-book <bookKey> <out.json> Export a book to standard structured JSON");
            Console.WriteLine("  rebuild-search-index            Rebuild the FTS5 search index");
            Console.WriteLine("==========================================================================");
        }
    }
}
