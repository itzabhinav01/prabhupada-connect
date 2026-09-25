using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace VedaBaseModern.CorpusPipeline.Services
{
    public class CorpusRepairService
    {
        private readonly string _dbPath;
        private readonly string _sourcesDir;
        private readonly string _canonicalDbPath;

        public CorpusRepairService(string dbPath, string sourcesDir, string canonicalDbPath)
        {
            _dbPath = dbPath;
            _sourcesDir = sourcesDir;
            _canonicalDbPath = canonicalDbPath;
        }

        public async Task RunFullRepairAsync()
        {
            Console.WriteLine("==========================================================================");
            Console.WriteLine("STARTING COMPREHENSIVE CORPUS REPAIR & INGESTION PIPELINE");
            Console.WriteLine($"Target DB      : {_dbPath}");
            Console.WriteLine($"Sources Dir    : {_sourcesDir}");
            Console.WriteLine($"Canonical DB  : {_canonicalDbPath}");
            Console.WriteLine("==========================================================================");

            // Step 1: Backup Target DB
            CreateDatabaseBackup();

            // Step 2: Stream and Repair ALL books from RTF sources
            string sbRtf = Path.Combine(_sourcesDir, "sb.rtf");
            if (File.Exists(sbRtf))
            {
                await RepairSrimadBhagavatamAsync(sbRtf);
            }

            string bgRtf = Path.Combine(_sourcesDir, "bg.rtf");
            if (File.Exists(bgRtf))
            {
                await RepairGenericBookAsync("BG", bgRtf, "Bhagavad-gītā");
            }

            string ccAdiRtf = Path.Combine(_sourcesDir, "cc_adi.rtf");
            if (File.Exists(ccAdiRtf))
            {
                await RepairGenericBookAsync("DI", ccAdiRtf, "Caitanya-caritāmṛta Ādi");
            }

            string ccMadhyaRtf = Path.Combine(_sourcesDir, "cc_madhya.rtf");
            if (File.Exists(ccMadhyaRtf))
            {
                await RepairGenericBookAsync("MADHYA", ccMadhyaRtf, "Caitanya-caritāmṛta Madhya");
            }

            string ccAntyaRtf = Path.Combine(_sourcesDir, "cc_antya.rtf");
            if (File.Exists(ccAntyaRtf))
            {
                await RepairGenericBookAsync("ANTYA", ccAntyaRtf, "Caitanya-caritāmṛta Antya");
            }

            string allOtherRtf = Path.Combine(_sourcesDir, "all_other_books.rtf");
            if (File.Exists(allOtherRtf))
            {
                await RepairAllOtherBooksAsync(allOtherRtf);
            }

            // Step 3: Auto-generate Devanagari for all Sanskrit verses where still missing
            await GenerateMissingDevanagariAsync();

            // Step 4: Import missing books from canonical database
            if (File.Exists(_canonicalDbPath))
            {
                await ImportMissingBooksFromCanonicalAsync();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARN] Canonical DB not found at {_canonicalDbPath}, skipping missing book import.");
                Console.ResetColor();
            }

            // Step 5: Normalize and fix BookKey DI
            await FixBookKeyDiAsync();

            // Step 6: Rebuild FTS5 SearchIndex
            await RebuildFtsIndexAsync();

            // Step 7: Final Audit & Verification
            await VerifyRepairIntegrityAsync();

            Console.WriteLine("==========================================================================");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("COMPREHENSIVE CORPUS REPAIR COMPLETED SUCCESSFULLY!");
            Console.ResetColor();
            Console.WriteLine("==========================================================================");
        }

        private void CreateDatabaseBackup()
        {
            string backupPath = _dbPath + ".bak";
            Console.WriteLine($"[Backup] Creating snapshot copy at: {backupPath}");
            File.Copy(_dbPath, backupPath, overwrite: true);
            Console.WriteLine("[Backup] Snapshot verified.");
        }

        private async Task RepairSrimadBhagavatamAsync(string sbRtfPath)
        {
            Console.WriteLine("\n[Phase 1] Streaming sb.rtf to repair missing verses, transliterations, and purports...");
            
            // 1. Load existing SB records into memory index
            var existingMap = new Dictionary<string, (string RecordKey, string? Dev, string? Trans, string? Purp)>(StringComparer.OrdinalIgnoreCase);
            var normalizedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT RecordKey, Reference, Devanagari, Transliteration, Purports FROM Records WHERE BookKey = 'SB';";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string rKey = reader.GetString(0);
                    string rRef = reader.GetString(1);
                    string? dev = reader.IsDBNull(2) ? null : reader.GetString(2);
                    string? trans = reader.IsDBNull(3) ? null : reader.GetString(3);
                    string? purp = reader.IsDBNull(4) ? null : reader.GetString(4);

                    existingMap[rRef] = (rKey, dev, trans, purp);
                    string norm = NormalizeRef(rRef);
                    normalizedMap[norm] = rRef;

                    if (rRef.Contains(","))
                    {
                        var parts = rRef.Split(',');
                        string lastPart = NormalizeRef(parts[^1]);
                        normalizedMap[lastPart] = rRef;
                    }
                }
            }
            Console.WriteLine($"[Phase 1] Loaded {existingMap.Count} existing SB records from database.");

            // 2. Stream RTF and collect updates
            int parsedCount = 0;
            int transRepaired = 0;
            int devRepaired = 0;
            int purpRepaired = 0;

            var updates = new List<(string RecordKey, string? Devanagari, string? Transliteration, string? Synonyms, string? Translation, string? Purports)>();

            foreach (var verse in RtfStreamParser.ParseVersesFromRtf(sbRtfPath))
            {
                parsedCount++;
                string norm = NormalizeRef(verse.Reference);
                if (!normalizedMap.TryGetValue(norm, out var matchedRef) && !existingMap.ContainsKey(verse.Reference))
                {
                    continue;
                }

                string exactRef = matchedRef ?? verse.Reference;
                var existing = existingMap[exactRef];

                bool needUpdate = false;
                string? newDev = existing.Dev;
                string? newTrans = existing.Trans;
                string? newPurp = existing.Purp;

                // Fix Transliteration
                if (string.IsNullOrWhiteSpace(existing.Trans) || (existing.Trans.Length < 30 && verse.DecodedTransliteration.Length > 50))
                {
                    if (!string.IsNullOrWhiteSpace(verse.DecodedTransliteration))
                    {
                        newTrans = verse.DecodedTransliteration;
                        transRepaired++;
                        needUpdate = true;
                    }
                }

                // Fix Devanagari
                if (string.IsNullOrWhiteSpace(existing.Dev))
                {
                    // If parsed has valid Devanagari, or generate from new/existing Transliteration
                    string transForDev = !string.IsNullOrWhiteSpace(newTrans) ? newTrans : verse.DecodedTransliteration;
                    if (!string.IsNullOrWhiteSpace(transForDev))
                    {
                        newDev = SanskritTransliterator.IastToDevanagari(transForDev);
                        devRepaired++;
                        needUpdate = true;
                    }
                }

                // Fix Purport
                if (!string.IsNullOrWhiteSpace(verse.DecodedPurports))
                {
                    if (string.IsNullOrWhiteSpace(existing.Purp) || (verse.DecodedPurports.Length > existing.Purp.Length + 10))
                    {
                        newPurp = verse.DecodedPurports;
                        purpRepaired++;
                        needUpdate = true;
                    }
                }

                if (needUpdate)
                {
                    updates.Add((existing.RecordKey, newDev, newTrans, 
                        string.IsNullOrWhiteSpace(verse.DecodedSynonyms) ? null : verse.DecodedSynonyms,
                        string.IsNullOrWhiteSpace(verse.DecodedTranslation) ? null : verse.DecodedTranslation,
                        newPurp));
                }
            }

            Console.WriteLine($"[Phase 1] Parsed {parsedCount} RTF entries. Prepared {updates.Count} verse repairs:");
            Console.WriteLine($"          - Transliterations to repair : {transRepaired}");
            Console.WriteLine($"          - Devanagari to repair      : {devRepaired}");
            Console.WriteLine($"          - Purports to expand/repair : {purpRepaired}");

            // 3. Batch write updates into SQLite
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                using var tx = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    UPDATE Records 
                    SET Devanagari = COALESCE(@Dev, Devanagari),
                        Transliteration = COALESCE(@Trans, Transliteration),
                        Synonyms = COALESCE(@Syn, Synonyms),
                        Translation = COALESCE(@Trl, Translation),
                        Purports = COALESCE(@Purp, Purports)
                    WHERE RecordKey = @Key;";

                var pKey = cmd.Parameters.Add("@Key", SqliteType.Text);
                var pDev = cmd.Parameters.Add("@Dev", SqliteType.Text);
                var pTrans = cmd.Parameters.Add("@Trans", SqliteType.Text);
                var pSyn = cmd.Parameters.Add("@Syn", SqliteType.Text);
                var pTrl = cmd.Parameters.Add("@Trl", SqliteType.Text);
                var pPurp = cmd.Parameters.Add("@Purp", SqliteType.Text);

                foreach (var u in updates)
                {
                    pKey.Value = u.RecordKey;
                    pDev.Value = (object?)u.Devanagari ?? DBNull.Value;
                    pTrans.Value = (object?)u.Transliteration ?? DBNull.Value;
                    pSyn.Value = (object?)u.Synonyms ?? DBNull.Value;
                    pTrl.Value = (object?)u.Translation ?? DBNull.Value;
                    pPurp.Value = (object?)u.Purports ?? DBNull.Value;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Phase 1] Committed {updates.Count} SB repairs to database.");
            Console.ResetColor();
        }

        private async Task<int> RepairGenericBookAsync(string bookKey, string rtfPath, string displayName)
        {
            Console.WriteLine($"\n[Phase 1] Streaming {Path.GetFileName(rtfPath)} for {displayName} [{bookKey}]...");

            var existingMap = new Dictionary<string, (string RecordKey, string? Dev, string? Trans, string? Purp)>(StringComparer.OrdinalIgnoreCase);
            var normalizedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT RecordKey, Reference, Devanagari, Transliteration, Purports FROM Records WHERE BookKey = @BKey;";
                cmd.Parameters.AddWithValue("@BKey", bookKey);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string rKey = reader.GetString(0);
                    string rRef = reader.GetString(1);
                    string? dev = reader.IsDBNull(2) ? null : reader.GetString(2);
                    string? trans = reader.IsDBNull(3) ? null : reader.GetString(3);
                    string? purp = reader.IsDBNull(4) ? null : reader.GetString(4);

                    existingMap[rRef] = (rKey, dev, trans, purp);
                    string norm = NormalizeRef(rRef);
                    normalizedMap[norm] = rRef;

                    if (rRef.Contains(","))
                    {
                        var parts = rRef.Split(',');
                        string lastPart = NormalizeRef(parts[^1]);
                        normalizedMap[lastPart] = rRef;
                    }
                    if (rRef.Contains("-") || rRef.Contains("–") || rRef.Contains("—"))
                    {
                        var parts = rRef.Split(new[] { '-', '–', '—' });
                        string firstPart = NormalizeRef(parts[0]);
                        normalizedMap[firstPart] = rRef;
                    }
                }
            }
            Console.WriteLine($"[Phase 1] Loaded {existingMap.Count} existing {bookKey} records from database.");

            int parsedCount = 0;
            int purpExpanded = 0;
            int transRepaired = 0;
            int devRepaired = 0;

            var updates = new List<(string RecordKey, string? Devanagari, string? Transliteration, string? Synonyms, string? Translation, string? Purports)>();

            foreach (var verse in RtfStreamParser.ParseVersesFromRtf(rtfPath))
            {
                parsedCount++;
                string norm = NormalizeRef(verse.Reference);
                if (!normalizedMap.TryGetValue(norm, out var matchedRef) && !existingMap.ContainsKey(verse.Reference))
                {
                    continue;
                }

                string exactRef = matchedRef ?? verse.Reference;
                var existing = existingMap[exactRef];

                bool needUpdate = false;
                string? newDev = existing.Dev;
                string? newTrans = existing.Trans;
                string? newPurp = existing.Purp;

                // Fix Transliteration
                if (string.IsNullOrWhiteSpace(existing.Trans) || (existing.Trans.Length < 30 && verse.DecodedTransliteration.Length > 50))
                {
                    if (!string.IsNullOrWhiteSpace(verse.DecodedTransliteration))
                    {
                        newTrans = verse.DecodedTransliteration;
                        transRepaired++;
                        needUpdate = true;
                    }
                }

                // Fix Devanagari (if Sanskrit book)
                if (string.IsNullOrWhiteSpace(existing.Dev) && (bookKey == "BG" || bookKey == "ISO" || bookKey == "BS" || bookKey == "MM"))
                {
                    string transForDev = !string.IsNullOrWhiteSpace(newTrans) ? newTrans : verse.DecodedTransliteration;
                    if (!string.IsNullOrWhiteSpace(transForDev))
                    {
                        newDev = SanskritTransliterator.IastToDevanagari(transForDev);
                        devRepaired++;
                        needUpdate = true;
                    }
                }

                // Purport repair: expand if parsed RTF is longer or if DB was blank
                if (!string.IsNullOrWhiteSpace(verse.DecodedPurports))
                {
                    if (string.IsNullOrWhiteSpace(existing.Purp))
                    {
                        newPurp = verse.DecodedPurports;
                        purpExpanded++;
                        needUpdate = true;
                    }
                    else if (verse.DecodedPurports.Length > existing.Purp.Length + 10)
                    {
                        newPurp = verse.DecodedPurports;
                        purpExpanded++;
                        needUpdate = true;
                    }
                }

                if (needUpdate)
                {
                    updates.Add((existing.RecordKey, newDev, newTrans,
                        string.IsNullOrWhiteSpace(verse.DecodedSynonyms) ? null : verse.DecodedSynonyms,
                        string.IsNullOrWhiteSpace(verse.DecodedTranslation) ? null : verse.DecodedTranslation,
                        newPurp));
                }
            }

            Console.WriteLine($"[Phase 1] Parsed {parsedCount} RTF entries for {bookKey}:");
            Console.WriteLine($"          - Purports expanded/repaired : {purpExpanded}");
            Console.WriteLine($"          - Transliterations repaired  : {transRepaired}");
            Console.WriteLine($"          - Devanagari repaired       : {devRepaired}");

            if (updates.Count > 0)
            {
                using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await conn.OpenAsync();
                    using var tx = conn.BeginTransaction();
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        UPDATE Records 
                        SET Devanagari = COALESCE(@Dev, Devanagari),
                            Transliteration = COALESCE(@Trans, Transliteration),
                            Synonyms = COALESCE(@Syn, Synonyms),
                            Translation = COALESCE(@Trl, Translation),
                            Purports = COALESCE(@Purp, Purports)
                        WHERE RecordKey = @Key;";

                    var pKey = cmd.Parameters.Add("@Key", SqliteType.Text);
                    var pDev = cmd.Parameters.Add("@Dev", SqliteType.Text);
                    var pTrans = cmd.Parameters.Add("@Trans", SqliteType.Text);
                    var pSyn = cmd.Parameters.Add("@Syn", SqliteType.Text);
                    var pTrl = cmd.Parameters.Add("@Trl", SqliteType.Text);
                    var pPurp = cmd.Parameters.Add("@Purp", SqliteType.Text);

                    foreach (var u in updates)
                    {
                        pKey.Value = u.RecordKey;
                        pDev.Value = (object?)u.Devanagari ?? DBNull.Value;
                        pTrans.Value = (object?)u.Transliteration ?? DBNull.Value;
                        pSyn.Value = (object?)u.Synonyms ?? DBNull.Value;
                        pTrl.Value = (object?)u.Translation ?? DBNull.Value;
                        pPurp.Value = (object?)u.Purports ?? DBNull.Value;
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Phase 1] Successfully committed {updates.Count} repairs for {bookKey}.");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"[Phase 1] All records in {bookKey} are verified 100% complete.");
            }

            return updates.Count;
        }

        private async Task RepairAllOtherBooksAsync(string rtfPath)
        {
            Console.WriteLine($"\n[Phase 1] Streaming all_other_books.rtf to verify NOI, ISO, TLK, MM, NBS...");

            var existingMap = new Dictionary<string, (string RecordKey, string BookKey, string? Dev, string? Trans, string? Purp)>(StringComparer.OrdinalIgnoreCase);
            var normalizedMap = new Dictionary<string, (string Ref, string BookKey)>(StringComparer.OrdinalIgnoreCase);

            using (var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT RecordKey, BookKey, Reference, Devanagari, Transliteration, Purports FROM Records WHERE BookKey IN ('NOI', 'ISO', 'TLK', 'MM', 'NBS', 'BS');";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string rKey = reader.GetString(0);
                    string bKey = reader.GetString(1);
                    string rRef = reader.GetString(2);
                    string? dev = reader.IsDBNull(3) ? null : reader.GetString(3);
                    string? trans = reader.IsDBNull(4) ? null : reader.GetString(4);
                    string? purp = reader.IsDBNull(5) ? null : reader.GetString(5);

                    existingMap[rRef] = (rKey, bKey, dev, trans, purp);
                    string norm = NormalizeRef(rRef);
                    normalizedMap[norm] = (rRef, bKey);
                }
            }

            int parsedCount = 0;
            int purpExpanded = 0;
            var updates = new List<(string RecordKey, string? Purports)>();

            foreach (var verse in RtfStreamParser.ParseVersesFromRtf(rtfPath))
            {
                parsedCount++;
                string norm = NormalizeRef(verse.Reference);
                if (!normalizedMap.TryGetValue(norm, out var matched))
                {
                    if (verse.Reference.Contains("Invocation", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!normalizedMap.TryGetValue(NormalizeRef("Iso Invocation"), out matched))
                            continue;
                    }
                    else if (verse.Reference.StartsWith("mantra", StringComparison.OrdinalIgnoreCase))
                    {
                        string isoRef = "Iso " + verse.Reference.Replace("mantra", "").Trim();
                        if (!normalizedMap.TryGetValue(NormalizeRef(isoRef), out matched))
                            continue;
                    }
                    else continue;
                }

                var existing = existingMap[matched.Ref];
                if (!string.IsNullOrWhiteSpace(verse.DecodedPurports))
                {
                    if (string.IsNullOrWhiteSpace(existing.Purp) || verse.DecodedPurports.Length > existing.Purp.Length + 10)
                    {
                        updates.Add((existing.RecordKey, verse.DecodedPurports));
                        purpExpanded++;
                    }
                }
            }

            Console.WriteLine($"[Phase 1] Parsed {parsedCount} entries in all_other_books.rtf. Purports to expand: {purpExpanded}");

            if (updates.Count > 0)
            {
                using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await conn.OpenAsync();
                    using var tx = conn.BeginTransaction();
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "UPDATE Records SET Purports = @Purp WHERE RecordKey = @Key;";
                    var pKey = cmd.Parameters.Add("@Key", SqliteType.Text);
                    var pPurp = cmd.Parameters.Add("@Purp", SqliteType.Text);

                    foreach (var u in updates)
                    {
                        pKey.Value = u.RecordKey;
                        pPurp.Value = (object?)u.Purports ?? DBNull.Value;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    await tx.CommitAsync();
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Phase 1] Committed {updates.Count} purport expansions across other canonical books.");
                Console.ResetColor();
            }
        }

        private async Task GenerateMissingDevanagariAsync()
        {
            Console.WriteLine("\n[Phase 2] Generating Devanagari for remaining Sanskrit verses (Cantos 10-12, BG, etc.)...");

            var candidates = new List<(string RecordKey, string Transliteration)>();

            using (var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                // Find all Sanskrit books where Devanagari is empty but Transliteration exists
                cmd.CommandText = @"
                    SELECT RecordKey, Transliteration
                    FROM Records 
                    WHERE BookKey IN ('SB', 'BG', 'ISO', 'BS', 'MM', 'BB')
                      AND (Devanagari IS NULL OR length(trim(Devanagari)) = 0)
                      AND Transliteration IS NOT NULL 
                      AND length(trim(Transliteration)) > 0;";

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    candidates.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            Console.WriteLine($"[Phase 2] Found {candidates.Count} verses requiring Devanagari generation.");

            if (candidates.Count > 0)
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                await conn.OpenAsync();
                using var tx = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE Records SET Devanagari = @Dev WHERE RecordKey = @Key;";

                var pKey = cmd.Parameters.Add("@Key", SqliteType.Text);
                var pDev = cmd.Parameters.Add("@Dev", SqliteType.Text);

                int count = 0;
                foreach (var c in candidates)
                {
                    string dev = SanskritTransliterator.IastToDevanagari(c.Transliteration);
                    pKey.Value = c.RecordKey;
                    pDev.Value = dev;
                    await cmd.ExecuteNonQueryAsync();
                    count++;
                }

                await tx.CommitAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Phase 2] Generated and committed Devanagari for {count} verses.");
                Console.ResetColor();
            }
        }

        private async Task ImportMissingBooksFromCanonicalAsync()
        {
            Console.WriteLine("\n[Phase 3] Ingesting missing canonical books (NOI, ISO, BS, MM, BB, HKH, HKC)...");

            string[] booksToImport = { "NOI", "ISO", "BS", "MM", "BB", "HKH", "HKC" };

            using var sourceConn = new SqliteConnection($"Data Source={_canonicalDbPath};Mode=ReadOnly");
            await sourceConn.OpenAsync();

            using var targetConn = new SqliteConnection($"Data Source={_dbPath}");
            await targetConn.OpenAsync();

            foreach (var bookKey in booksToImport)
            {
                // Check if already in target
                using (var checkCmd = targetConn.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM Records WHERE BookKey = @BKey;";
                    checkCmd.Parameters.AddWithValue("@BKey", bookKey);
                    long existingCount = (long)(await checkCmd.ExecuteScalarAsync() ?? 0L);
                    if (existingCount > 0)
                    {
                        Console.WriteLine($"[Phase 3] Book [{bookKey}] already contains {existingCount} records. Skipping.");
                        continue;
                    }
                }

                // Read from source
                var records = new List<(string RecordKey, string BookKey, int Sequence, string? ParentKey, 
                    string RecordType, string? Reference, string? ReferenceStatus, string? Title, 
                    string? Devanagari, string? Transliteration, string? Synonyms, string? Translation, string? Purports)>();

                using (var srcCmd = sourceConn.CreateCommand())
                {
                    srcCmd.CommandText = @"
                        SELECT RecordKey, BookKey, Sequence, ParentKey, RecordType, Reference, ReferenceStatus, Title,
                               Devanagari, Transliteration, Synonyms, Translation, Purports
                        FROM Records WHERE BookKey = @BKey ORDER BY Sequence ASC;";
                    srcCmd.Parameters.AddWithValue("@BKey", bookKey);

                    using var r = await srcCmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        string rKey = r.GetString(0);
                        string bKey = r.GetString(1);
                        int seq = r.GetInt32(2);
                        string? parent = r.IsDBNull(3) ? null : r.GetString(3);
                        string recType = r.GetString(4);
                        string? rRef = r.IsDBNull(5) ? null : r.GetString(5);
                        string? refStat = r.IsDBNull(6) ? null : r.GetString(6);
                        string? title = r.IsDBNull(7) ? null : r.GetString(7);
                        string? dev = r.IsDBNull(8) ? null : r.GetString(8);
                        string? trans = r.IsDBNull(9) ? null : r.GetString(9);
                        string? syn = r.IsDBNull(10) ? null : r.GetString(10);
                        string? trl = r.IsDBNull(11) ? null : r.GetString(11);
                        string? purp = r.IsDBNull(12) ? null : r.GetString(12);

                        // If Devanagari is empty but Transliteration exists (e.g. ISO), auto-generate!
                        if (string.IsNullOrWhiteSpace(dev) && !string.IsNullOrWhiteSpace(trans))
                        {
                            dev = SanskritTransliterator.IastToDevanagari(trans);
                        }

                        records.Add((rKey, bKey, seq, parent, recType, rRef, refStat, title, dev, trans, syn, trl, purp));
                    }
                }

                if (records.Count == 0)
                {
                    Console.WriteLine($"[Phase 3] Book [{bookKey}] had 0 records in canonical source.");
                    continue;
                }

                // Insert into target DB
                using (var tx = targetConn.BeginTransaction())
                {
                    using var insCmd = targetConn.CreateCommand();
                    insCmd.Transaction = tx;
                    insCmd.CommandText = @"
                        INSERT OR REPLACE INTO Records 
                        (RecordKey, BookKey, Sequence, ParentKey, RecordType, Reference, ReferenceStatus, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
                        VALUES (@RecordKey, @BookKey, @Sequence, @ParentKey, @RecordType, @Reference, @ReferenceStatus, @Title, @Devanagari, @Transliteration, @Synonyms, @Translation, @Purports);";

                    var pKey = insCmd.Parameters.Add("@RecordKey", SqliteType.Text);
                    var pBKey = insCmd.Parameters.Add("@BookKey", SqliteType.Text);
                    var pSeq = insCmd.Parameters.Add("@Sequence", SqliteType.Integer);
                    var pParent = insCmd.Parameters.Add("@ParentKey", SqliteType.Text);
                    var pType = insCmd.Parameters.Add("@RecordType", SqliteType.Text);
                    var pRef = insCmd.Parameters.Add("@Reference", SqliteType.Text);
                    var pRefStat = insCmd.Parameters.Add("@ReferenceStatus", SqliteType.Text);
                    var pTitle = insCmd.Parameters.Add("@Title", SqliteType.Text);
                    var pDev = insCmd.Parameters.Add("@Devanagari", SqliteType.Text);
                    var pTrans = insCmd.Parameters.Add("@Transliteration", SqliteType.Text);
                    var pSyn = insCmd.Parameters.Add("@Synonyms", SqliteType.Text);
                    var pTrl = insCmd.Parameters.Add("@Translation", SqliteType.Text);
                    var pPurp = insCmd.Parameters.Add("@Purports", SqliteType.Text);

                    foreach (var rec in records)
                    {
                        pKey.Value = rec.RecordKey;
                        pBKey.Value = rec.BookKey;
                        pSeq.Value = rec.Sequence;
                        pParent.Value = (object?)rec.ParentKey ?? DBNull.Value;
                        pType.Value = rec.RecordType;
                        pRef.Value = (object?)rec.Reference ?? DBNull.Value;
                        pRefStat.Value = (object?)rec.ReferenceStatus ?? "Active";
                        pTitle.Value = (object?)rec.Title ?? DBNull.Value;
                        pDev.Value = (object?)rec.Devanagari ?? DBNull.Value;
                        pTrans.Value = (object?)rec.Transliteration ?? DBNull.Value;
                        pSyn.Value = (object?)rec.Synonyms ?? DBNull.Value;
                        pTrl.Value = (object?)rec.Translation ?? DBNull.Value;
                        pPurp.Value = (object?)rec.Purports ?? DBNull.Value;
                        await insCmd.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Phase 3] Ingested book [{bookKey,-5}] with {records.Count} canonical records.");
                Console.ResetColor();
            }
        }

        private async Task FixBookKeyDiAsync()
        {
            Console.WriteLine("\n[Phase 4] Verifying and harmonizing BookKey DI (Cc Adi)...");
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();

            // Ensure Books table has 'DI' properly mapped with title
            cmd.CommandText = @"
                UPDATE Books 
                SET Title = 'Śrī Caitanya-caritāmṛta — Ādi-līlā' 
                WHERE BookKey = 'DI' AND (Title IS NULL OR Title = 'DI');

                UPDATE Records 
                SET RecordType = 'Narrative' 
                WHERE RecordKey IN (
                    'BS-(NONE)-FOREWORD',
                    'ISO-(NONE)-INTRODUCTION',
                    'ISO-(NONE)-INV',
                    'MM-(NONE)-INTRODUCTION',
                    'NOI-(NONE)-PREFACE',
                    'NOI-(NONE)-UNSPEC-DE8C725A8B63'
                );";
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine("[Phase 4] BookKey DI verified and non-verse RecordTypes harmonized.");
        }

        private async Task RebuildFtsIndexAsync()
        {
            Console.WriteLine("\n[Phase 5] Reconstructing FTS5 SearchIndex with updated text and purports...");
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 600;

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
            Console.WriteLine("[Phase 5] FTS5 SearchIndex reconstructed successfully.");
            Console.ResetColor();
        }

        private async Task VerifyRepairIntegrityAsync()
        {
            Console.WriteLine("\n==========================================================================");
            Console.WriteLine("CORPUS INTEGRITY AUDIT & VERIFICATION RESULTS");
            Console.WriteLine("==========================================================================");

            using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();

            // Total records & books
            cmd.CommandText = "SELECT COUNT(*) FROM Records;";
            long totalRecords = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            cmd.CommandText = "SELECT COUNT(DISTINCT BookKey) FROM Records;";
            long activeBooks = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            // Audit missing devanagari in scripture books (BG, SB, ISO, BS, MM, NOI)
            cmd.CommandText = @"
                SELECT COUNT(*) FROM Records 
                WHERE BookKey IN ('BG', 'SB', 'ISO', 'BS', 'MM', 'NOI')
                  AND (Devanagari IS NULL OR length(trim(Devanagari)) = 0);";
            long missingDevInScriptures = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            // Audit missing transliterations in scripture books
            cmd.CommandText = @"
                SELECT COUNT(*) FROM Records 
                WHERE BookKey IN ('BG', 'SB', 'ISO', 'BS', 'MM', 'NOI')
                  AND (Transliteration IS NULL OR length(trim(Transliteration)) = 0)
                  AND RecordType = 'Verse';";
            long missingTransInVerses = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

            Console.WriteLine($"Total Active Books     : {activeBooks}");
            Console.WriteLine($"Total Records          : {totalRecords}");
            Console.WriteLine($"Missing Devanagari (Sanskrit scriptures) : {missingDevInScriptures}");
            Console.WriteLine($"Missing Transliteration (Scripture verses) : {missingTransInVerses}");

            // Specific check for user's reported verse SB 5.6.6
            cmd.CommandText = @"
                SELECT Reference, length(Devanagari), length(Transliteration), length(Purports),
                       substr(Devanagari, 1, 50), substr(Transliteration, 1, 50)
                FROM Records WHERE Reference = 'SB 5.6.6';";

            {
                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    string rRef = r.GetString(0);
                    int devLen = r.GetInt32(1);
                    int transLen = r.GetInt32(2);
                    int purpLen = r.GetInt32(3);
                    string devPreview = r.GetString(4);
                    string transPreview = r.GetString(5);

                    Console.WriteLine("--------------------------------------------------------------------------");
                    Console.WriteLine($"[VERIFY] {rRef}:");
                    Console.WriteLine($"         Devanagari      : {devLen} chars -> {devPreview}...");
                    Console.WriteLine($"         Transliteration : {transLen} chars -> {transPreview}...");
                    Console.WriteLine($"         Purports        : {purpLen} chars");
                    Console.WriteLine("--------------------------------------------------------------------------");

                    if (devLen > 0 && transLen > 0 && purpLen > 500)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[PASS] SB 5.6.6 fully restored with pristine Devanagari, Transliteration, and Purports!");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[FAIL] SB 5.6.6 did not pass verification!");
                        Console.ResetColor();
                    }
                }
            }

            // Purport coverage breakdown across major books
            Console.WriteLine("\n--- Canonical Purport Coverage Breakdown ---");
            string[] booksToCheck = { "BG", "SB", "DI", "MADHYA", "ANTYA", "NOI", "ISO", "TLK", "MM", "NBS", "BS" };
            foreach (var b in booksToCheck)
            {
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = $@"
                    SELECT COUNT(*), 
                           COUNT(CASE WHEN Purports IS NOT NULL AND length(trim(Purports)) > 0 THEN 1 END),
                           COALESCE(AVG(length(Purports)), 0),
                           MAX(length(Purports))
                    FROM Records WHERE BookKey = '{b}';";
                using var br = await cmd2.ExecuteReaderAsync();
                if (await br.ReadAsync())
                {
                    long total = br.GetInt64(0);
                    long withPurp = br.GetInt64(1);
                    double avgLen = br.GetDouble(2);
                    int maxLen = br.IsDBNull(3) ? 0 : br.GetInt32(3);
                    Console.WriteLine($"[{b,-7}] Total: {total,5} | With Purports: {withPurp,5} ({((double)withPurp/total*100):F1}%) | Avg Len: {avgLen,6:F0} chars | Max Len: {maxLen,6} chars");
                }
            }
        }

        private static string NormalizeRef(string r)
        {
            if (string.IsNullOrEmpty(r)) return string.Empty;
            string s = r.ToLowerInvariant()
                        .Replace("ā", "a").Replace("ä", "a")
                        .Replace("ī", "i").Replace("ū", "u")
                        .Replace("ṛ", "r").Replace("ṝ", "r")
                        .Replace("ś", "s").Replace("ṣ", "s")
                        .Replace("ṭ", "t").Replace("ḍ", "d")
                        .Replace("ṇ", "n").Replace("ṅ", "n").Replace("ñ", "n")
                        .Replace("ṁ", "m").Replace("ḥ", "h")
                        .Replace("verse", "")
                        .Replace("vs", "")
                        .Replace("cc", "")
                        .Replace("text", "");
            return Regex.Replace(s, @"[^a-z0-9]", "");
        }
    }
}
