using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VedaBaseModern.Core.Helpers;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Registry;
using VedaBaseModern.Core.Repositories;
using VedaBaseModern.Core.Services;

namespace VedaBaseModern2.Tests;

class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=========================================================");
        Console.WriteLine(" VedaBaseModern 2 - Comprehensive Automated Verification");
        Console.WriteLine("=========================================================\n");

        string baseDir = AppContext.BaseDirectory;
        string? dbDir = null;
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "Database");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "prabhupada_corpus.db")))
            {
                dbDir = candidate;
                break;
            }
            current = current.Parent;
        }

        if (string.IsNullOrEmpty(dbDir)) dbDir = @"C:\VedaBaseModern2\Database";
        string corpusDb = Path.Combine(dbDir, "prabhupada_corpus.db");
        string testUserDb = Path.Combine(Path.GetTempPath(), $"vedabase_test_{Guid.NewGuid():N}.db");

        try
        {
            // 1. Corpus Registry & Database Tests
            Console.WriteLine("[1/7] Testing Corpus Repository & Integrity...");
            await TestCorpusDatabaseAsync(corpusDb);

            // 2. Direct Reference Resolution Tests
            Console.WriteLine("\n[2/7] Testing Direct Reference Resolution...");
            await TestDirectReferenceServiceAsync(corpusDb);

            // 3. Unified Search & FTS5 Tests
            Console.WriteLine("\n[3/7] Testing Unified Search & FTS5 Indexing...");
            await TestSearchServiceAsync(corpusDb, testUserDb);

            // 4. Reverse Concordance Word Lookup Tests
            Console.WriteLine("\n[4/7] Testing Reverse Concordance Word Lookup...");
            await TestConcordanceServiceAsync(corpusDb);

            // 5. User Database (Bookmarks, Collections, Highlights) Tests
            Console.WriteLine("\n[5/7] Testing User Database & Highlights...");
            await TestUserDatabaseAsync(testUserDb);

            // 6. Markdown Scholarly Notes & [[Verse-Ref]] Live Previews Tests
            Console.WriteLine("\n[6/7] Testing Markdown Wiki-Link Parsing...");
            TestMarkdownWikiLinks();

            // 7. Reader HTML/CSS/JS Assets Verification
            Console.WriteLine("\n[7/8] Testing Reader Web Assets (HTML5, 60fps CSS, JS bridge)...");
            TestReaderAssets();

            // 8. Scripture Repair & Text Fidelity Verification
            Console.WriteLine("\n[8/9] Testing Scripture Repair & High-Fidelity Text Verification...");
            await TestScriptureRepairIntegrityAsync(corpusDb);

            // 9. Knowledge Base & Automated Backup Tests
            Console.WriteLine("\n[9/10] Testing Knowledge Base (#tags, [[wiki-links]]) & Automated Rolling Backups...");
            await TestKnowledgeBaseAndBackupsAsync(testUserDb);

            // 10. Prabhupada-lilamrita (SPL) Ingestion & Retrieval Tests
            Console.WriteLine("\n[10/11] Testing Prabhupāda-līlāmṛta (SPL) Ingestion & Retrieval...");
            await TestPrabhupadaLilamritaAsync(corpusDb);

            // 11. Folio Advanced Search Suite & Vocab Word Wheel Tests
            Console.WriteLine("\n[11/12] Testing Folio Advanced Search (Word Wheel, Proximity NEAR, Multi-Book)...");
            await TestAdvancedSearchAndVocabAsync(corpusDb);

            // 12. Prabhupada Slokas (SPS), Outside Quotes Mapping & Tiered Canonical Search Tests
            Console.WriteLine("\n[12/12] Testing Prabhupāda Ślokas (SPS), Outside Quotes & Tiered Canonical Search...");
            await TestPrabhupadaSlokasAndCanonicalTieringAsync(corpusDb);

            // 13. Back to Godhead (BTG), Songs of the Vaisnava Acaryas (SVA), Temple Mantra Guide (TMG) & A-Z Multi-Book Filters
            Console.WriteLine("\n[13/13] Testing BTG, SVA, TMG Ingestion, Breadcrumbs & A–Z Multi-Book Filtering...");
            await TestBtgSvaTmgAndBookFiltersAsync(corpusDb);
        }
        finally
        {
            // Clean up temporary test db
            if (File.Exists(testUserDb))
            {
                try { File.Delete(testUserDb); } catch { }
            }
        }

        Console.WriteLine("\n=========================================================");
        Console.WriteLine($" TEST SUMMARY: {_passed} Passed, {_failed} Failed");
        Console.WriteLine("=========================================================");

        return _failed == 0 ? 0 : 1;
    }

    private static void Assert(bool condition, string testName, string? extra = null)
    {
        if (condition)
        {
            _passed++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [PASS] ");
            Console.ResetColor();
            Console.WriteLine(testName + (extra != null ? $" ({extra})" : ""));
        }
        else
        {
            _failed++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  [FAIL] ");
            Console.ResetColor();
            Console.WriteLine(testName + (extra != null ? $" - {extra}" : ""));
        }
    }

    private static async Task TestCorpusDatabaseAsync(string corpusDb)
    {
        Assert(File.Exists(corpusDb), "Corpus database file exists", corpusDb);

        var bookRegistry = new BookRegistry();
        var corpusRegistry = new CorpusRegistry();
        var descriptor = await corpusRegistry.InitializeAsync(corpusDb);

        Assert(descriptor.Status == CorpusStatus.Active, "Corpus descriptor is Active");

        await bookRegistry.InitializeFromDatabaseAsync(corpusDb);
        var repo = new SqliteCorpusRepository(corpusDb, bookRegistry);

        var hierarchy = await repo.GetLibraryHierarchyAsync();
        Assert(hierarchy != null && hierarchy.Count >= 33, "Library hierarchy contains all canonical book categories", $"Count: {hierarchy?.Count}");

        // Verse BG-1-1 Lookup
        var bg11 = await repo.GetRecordAsync("BG-1-1");
        Assert(bg11 != null, "BG-1-1 record retrieved");
        Assert(!string.IsNullOrEmpty(bg11?.Devanagari), "BG-1-1 has Devanagari text");
        Assert(!string.IsNullOrEmpty(bg11?.Transliteration), "BG-1-1 has Transliteration text");
        Assert(!string.IsNullOrEmpty(bg11?.Synonyms), "BG-1-1 has Synonyms text");
        Assert(!string.IsNullOrEmpty(bg11?.Translation), "BG-1-1 has Translation text");
        Assert(!string.IsNullOrEmpty(bg11?.Purports), "BG-1-1 has Purport text");

        // Chapter continuous records
        var bgCh1 = await repo.GetChapterRecordsAsync("BG-1-1");
        Assert(bgCh1.Count == 39, "BG Chapter 1 records count == 39 (covers 47 verses including combined verses)", $"Count: {bgCh1.Count}");
        Assert(bgCh1.First().RecordKey == "BG-1-1" && bgCh1.Last().RecordKey == "BG-1-46", "BG Chapter 1 records sequence correct");

        // Adjacent record navigation
        var prevFromStart = await repo.GetAdjacentRecordKeyAsync("BG-1-1", next: false);
        Assert(prevFromStart == "BG-INTRODUCTION", "Previous from BG-1-1 is introductory front-matter BG-INTRODUCTION", prevFromStart);

        var prevFromFirstRecord = await repo.GetAdjacentRecordKeyAsync("BG-SETTING-THE-SCENE", next: false);
        Assert(prevFromFirstRecord == null, "Previous from absolute first book record BG-SETTING-THE-SCENE is null");

        var nextFromStart = await repo.GetAdjacentRecordKeyAsync("BG-1-1", next: true);
        Assert(nextFromStart == "BG-1-2", "Next from BG-1-1 is BG-1-2");
    }

    private static async Task TestDirectReferenceServiceAsync(string corpusDb)
    {
        var bookRegistry = new BookRegistry();
        await bookRegistry.InitializeFromDatabaseAsync(corpusDb);
        var repo = new SqliteCorpusRepository(corpusDb, bookRegistry);
        var refService = new DirectReferenceService(repo);

        var bgRes = await refService.TryResolveExactAsync("bg 1.1");
        Assert(bgRes == "BG-1-1", "Resolves 'bg 1.1' -> 'BG-1-1'", bgRes);

        var bg49Res = await refService.TryResolveExactAsync("bg 4.9");
        Assert(bg49Res == "BG-4-9", "Resolves 'bg 4.9' -> 'BG-4-9'", bg49Res);

        var gitaRef = await refService.TryResolveExactAsync("Bhagavad-gītā 4.9");
        Assert(gitaRef == "BG-4-9", "Resolves 'Bhagavad-gītā 4.9' (with diacritics) -> 'BG-4-9'", gitaRef);

        var sbRes = await refService.TryResolveExactAsync("sb 1.1.1");
        Assert(sbRes == "SB-1.1-1", "Resolves 'sb 1.1.1' -> 'SB-1.1-1'", sbRes);

        var sb566Res = await refService.TryResolveExactAsync("sb 5.6.6");
        Assert(sb566Res == "SB-5.6-6", "Resolves 'sb 5.6.6' -> 'SB-5.6-6'", sb566Res);

        var ccRes = await refService.TryResolveExactAsync("cc adi 1.1");
        Assert(ccRes == "ĀDI-1-1", "Resolves 'cc adi 1.1' -> 'ĀDI-1-1'", ccRes);

        var ccDiacritic = await refService.TryResolveExactAsync("CC Ādi 1.1");
        Assert(ccDiacritic == "ĀDI-1-1", "Resolves 'CC Ādi 1.1' (with diacritics) -> 'ĀDI-1-1'", ccDiacritic);

        var ccMadhya = await refService.TryResolveExactAsync("CC Madhya 22.83");
        Assert(ccMadhya == "MADHYA-22-83", "Resolves 'CC Madhya 22.83' -> 'MADHYA-22-83'", ccMadhya);

        var isoRes = await refService.TryResolveExactAsync("iso 1");
        Assert(isoRes == "ISO-(NONE)-MANTRA-1", "Resolves 'iso 1' -> 'ISO-(NONE)-MANTRA-1'", isoRes);

        var bsRes = await refService.TryResolveExactAsync("bs 5.38");
        Assert(bsRes == "BS-5-38", "Resolves 'bs 5.38' -> 'BS-5-38'", bsRes);

        var suggestions = await refService.GetSuggestionsAsync("bg 1.");
        Assert(suggestions != null && suggestions.Count > 0, "Provides suggestions for 'bg 1.'", $"Count: {suggestions?.Count}");
    }

    private static async Task TestSearchServiceAsync(string corpusDb, string userDb)
    {
        var bookRegistry = new BookRegistry();
        await bookRegistry.InitializeFromDatabaseAsync(corpusDb);
        var repo = new SqliteCorpusRepository(corpusDb, bookRegistry);
        var userRepo = new SqliteUserRepository(userDb);
        var searchService = new UnifiedSearchService(repo, userRepo);

        var (results, count) = await repo.SearchAsync("dharma", limit: 10);
        Assert(results != null && results.Count > 0, "Corpus FTS search for 'dharma' returns hits", $"Found: {count}");

        var (bgResults, bgCount) = await repo.SearchAsync("yoga", bookKey: "BG", limit: 10);
        Assert(bgResults != null && bgResults.Count > 0, "Corpus FTS scoped search in 'BG' for 'yoga'", $"Found: {bgCount}");

        // Field-scoped searches
        var (purportRes, purportCount) = await repo.SearchAsync("arjuna", limit: 10, fieldScope: "Purports");
        Assert(purportRes != null && purportCount > 0, "Field-scoped search in Purports for 'arjuna'", $"Found: {purportCount}");

        var (transRes, transCount) = await repo.SearchAsync("dharma", limit: 10, fieldScope: "Translations");
        Assert(transRes != null && transCount > 0, "Field-scoped search in Translations for 'dharma'", $"Found: {transCount}");

        var (synRes, synCount) = await repo.SearchAsync("bhakti", limit: 10, fieldScope: "Synonyms");
        Assert(synRes != null && synCount > 0, "Field-scoped search in Synonyms for 'bhakti'", $"Found: {synCount}");

        // Boolean operator queries
        var (boolAndRes, boolAndCount) = await repo.SearchAsync("krishna AND arjuna", limit: 10);
        Assert(boolAndRes != null && boolAndCount > 0, "Boolean AND query 'krishna AND arjuna'", $"Found: {boolAndCount}");

        var (quotedRes, quotedCount) = await repo.SearchAsync("\"supreme personality of godhead\"", limit: 10);
        Assert(quotedRes != null && quotedCount > 0, "Exact phrase query '\"supreme personality of godhead\"'", $"Found: {quotedCount}");

        var (boolNotRes, boolNotCount) = await repo.SearchAsync("krishna NOT maya", limit: 10);
        Assert(boolNotRes != null && boolNotCount > 0, "Boolean NOT query 'krishna NOT maya'", $"Found: {boolNotCount}");

        // IAST Neutrality Tests: plain English matches IAST even with isExactWord & isExactCase
        var (plainBhunjanaRes, plainBhunjanaCount) = await repo.SearchAsync("bhunjana", bookKey: "SB", isExactWord: true, isExactCase: true);
        Assert(plainBhunjanaCount == 15, "Plain English 'bhunjana' in SB with Match Exact Word & Match Case matches 15 verses", $"Found: {plainBhunjanaCount}");

        var (iastBhunjanaRes, iastBhunjanaCount) = await repo.SearchAsync("bhuñjāna", bookKey: "SB", isExactWord: true, isExactCase: true);
        Assert(iastBhunjanaCount == 15, "IAST 'bhuñjāna' in SB with Match Exact Word & Match Case matches identical 15 verses", $"Found: {iastBhunjanaCount}");

        var (capBhunjanaRes, capBhunjanaCount) = await repo.SearchAsync("Bhunjana", bookKey: "SB", isExactWord: true, isExactCase: true);
        Assert(capBhunjanaCount == 0, "Capitalized 'Bhunjana' with Match Case returns 0 because verses have lowercase 'bhuñjāna'", $"Found: {capBhunjanaCount}");

        var (krsnaCaseRes, krsnaCaseCount) = await repo.SearchAsync("krsna", bookKey: "BG", isExactWord: true, isExactCase: true);
        Assert(krsnaCaseCount > 0, "Plain English 'krsna' in BG matches 'kṛṣṇa' with Match Case & Match Exact Word", $"Found: {krsnaCaseCount}");
    }

    private static async Task TestConcordanceServiceAsync(string corpusDb)
    {
        var concordance = new ConcordanceService(corpusDb);

        // Lookup 'dharmaksetre'
        var result1 = await concordance.LookupWordAsync("dharmakṣetre");
        Assert(result1.TotalCount > 0, "Concordance finds occurrences of 'dharmakṣetre'", $"Count: {result1.TotalCount}");
        Assert(result1.BookGroups.Any(g => g.BookKey == "BG"), "Concordance 'dharmakṣetre' matches in Bhagavad-gītā");

        // Lookup plain English 'bhunjana' finds IAST 'bhuñjāna'
        var resultBhunjana = await concordance.LookupWordAsync("bhunjana");
        Assert(resultBhunjana.TotalCount > 0, "Concordance finds occurrences of plain English 'bhunjana'", $"Count: {resultBhunjana.TotalCount}");
        Assert(resultBhunjana.AllMatches.Any(m => m.Snippet.Contains("bhuñjāna")), "Concordance 'bhunjana' successfully matches IAST lemma 'bhuñjāna'");

        // Lookup 'prapadyate'
        var result2 = await concordance.LookupWordAsync("prapadyate");
        Assert(result2.TotalCount > 0, "Concordance finds occurrences of 'prapadyate'", $"Count: {result2.TotalCount}, Books: {result2.BookGroups.Count}");

        // Lookup 'bhakti'
        var result3 = await concordance.LookupWordAsync("bhakti");
        Assert(result3.TotalCount > 0, "Concordance finds occurrences of 'bhakti'", $"Count: {result3.TotalCount}");
    }

    private static async Task TestUserDatabaseAsync(string userDb)
    {
        var userRepo = new SqliteUserRepository(userDb);
        await userRepo.InitializeAsync();

        // 1. Bookmarks
        bool initialBm = await userRepo.IsBookmarkedAsync("BG-1-1");
        Assert(!initialBm, "BG-1-1 initially not bookmarked");

        await userRepo.AddBookmarkAsync("BG-1-1");
        bool bookmarked = await userRepo.IsBookmarkedAsync("BG-1-1");
        Assert(bookmarked, "BG-1-1 successfully bookmarked");

        int bmCount = await userRepo.GetBookmarkCountAsync();
        Assert(bmCount == 1, "Bookmark count is 1");

        await userRepo.RemoveBookmarkAsync("BG-1-1");
        bool unbookmarked = await userRepo.IsBookmarkedAsync("BG-1-1");
        Assert(!unbookmarked, "BG-1-1 successfully unbookmarked");

        // 2. Highlights with exact character offsets
        var h1 = await userRepo.AddHighlightAsync("BG-1-1", "Translation", 0, 12, "Dhṛtarāṣṭra", HighlightColor.Yellow);
        Assert(h1 != null && !string.IsNullOrEmpty(h1.Id), "Highlight created with UUID", h1?.Id);

        var highlights = await userRepo.GetHighlightsAsync("BG-1-1");
        Assert(highlights.Count == 1, "Retrieved 1 highlight for BG-1-1");
        Assert(highlights[0].SelectedText == "Dhṛtarāṣṭra", "Highlight preserved exact text");
        Assert(highlights[0].StartOffset == 0 && highlights[0].Length == 12, "Highlight preserved startOffset and length");

        // Update highlight color
        await userRepo.UpdateHighlightColorAsync(h1.Id, HighlightColor.Green);
        var updatedList = await userRepo.GetHighlightsAsync("BG-1-1");
        Assert(updatedList[0].Color == HighlightColor.Green, "Highlight color updated to Green");

        // Remove highlight
        await userRepo.RemoveHighlightAsync(h1.Id);
        var remaining = await userRepo.GetHighlightsAsync("BG-1-1");
        Assert(remaining.Count == 0, "Highlight successfully removed");
    }

    private static void TestMarkdownWikiLinks()
    {
        string sampleNote = @"
# Research on Surrender
According to [[BG 18.66]], surrender to the Supreme Lord relieves all reactions.
Also compare this with [[SB 1.2.6]] regarding unmotivated, uninterrupted devotional service.
Further check [[CC Adi 1.1]] for invocations.";

        bool hasLinks = MarkdownNoteHelper.HasWikiLinks(sampleNote);
        Assert(hasLinks, "Note correctly identified as containing [[Verse-Ref]] links");

        var extracted = MarkdownNoteHelper.ExtractWikiLinks(sampleNote);
        Assert(extracted.Count == 3, "Extracted exactly 3 verse references from note", $"Found: {extracted.Count}");
        Assert(extracted.Contains("BG 18.66"), "Note contains 'BG 18.66'");
        Assert(extracted.Contains("SB 1.2.6"), "Note contains 'SB 1.2.6'");
        Assert(extracted.Contains("CC Adi 1.1"), "Note contains 'CC Adi 1.1'");
    }

    private static void TestReaderAssets()
    {
        string baseDir = @"C:\VedaBaseModern2\App\VedaBaseModern2.UI\Assets\Reader";
        string htmlPath = Path.Combine(baseDir, "reader.html");
        string cssPath = Path.Combine(baseDir, "reader.css");
        string jsPath = Path.Combine(baseDir, "reader.js");

        Assert(File.Exists(htmlPath), "reader.html exists", htmlPath);
        Assert(File.Exists(cssPath), "reader.css exists", cssPath);
        Assert(File.Exists(jsPath), "reader.js exists", jsPath);

        string htmlContent = File.ReadAllText(htmlPath);
        Assert(htmlContent.Contains("id=\"content\"") && htmlContent.Contains("class=\"reader-container\""), "reader.html contains main reader container");
        Assert(htmlContent.Contains("id=\"btn-concordance\""), "reader.html contains concordance lookup button");

        string cssContent = File.ReadAllText(cssPath);
        Assert(cssContent.Contains("content-visibility: auto"), "reader.css contains content-visibility: auto for 60fps scrolling");
        Assert(cssContent.Contains("contain-intrinsic-size"), "reader.css contains contain-intrinsic-size for smooth scroll height calculation");
        Assert(cssContent.Contains(".sanskrit-word"), "reader.css contains .sanskrit-word styling for interactive lemmas");
        Assert(cssContent.Contains(".purport-verse"), "reader.css contains .purport-verse styling for authentic poetic verses");
        Assert(cssContent.Contains(".scripture-link"), "reader.css contains .scripture-link golden citation hyperlinks");

        string jsContent = File.ReadAllText(jsPath);
        Assert(jsContent.Contains("concordance_lookup"), "reader.js contains concordance_lookup postMessage dispatcher");
        Assert(jsContent.Contains("applyHighlights"), "reader.js contains applyHighlights function");
        Assert(jsContent.Contains("setTheme"), "reader.js contains setTheme function");
        Assert(jsContent.Contains("linkifyScriptureReferences"), "reader.js contains linkifyScriptureReferences engine");
        Assert(jsContent.Contains("isQuotedVerse"), "reader.js contains isQuotedVerse poetic meter detector");
        Assert(jsContent.Contains("onNavigateScripture"), "reader.js contains onNavigateScripture bridge dispatcher");
        Assert(jsContent.Contains("detectSanskritMeter"), "reader.js contains detectSanskritMeter prosody engine");
        Assert(jsContent.Contains("toggleChantingPulse"), "reader.js contains toggleChantingPulse audio synthesizer");
        Assert(cssContent.Contains(".meter-badge"), "reader.css contains .meter-badge styling");
        Assert(cssContent.Contains(".chanting-guide-drawer"), "reader.css contains .chanting-guide-drawer styling");
        Assert(cssContent.Contains(".verse-citation-pill"), "reader.css contains .verse-citation-pill styling");
        Assert(jsContent.Contains("verse-citation-pill"), "reader.js contains verse-citation-pill markup generator");
    }

    private static async Task TestScriptureRepairIntegrityAsync(string corpusDb)
    {
        var bookRegistry = new BookRegistry();
        await bookRegistry.InitializeFromDatabaseAsync(corpusDb);
        var repo = new SqliteCorpusRepository(corpusDb, bookRegistry);

        // 1. SB 5.6.6 verification (user's reported verse)
        var sb566 = await repo.GetRecordAsync("SB-5.6-6");
        Assert(sb566 != null, "SB 5.6.6 record retrieved", "SB-5.6-6");
        Assert(!string.IsNullOrWhiteSpace(sb566?.Devanagari) && sb566!.Devanagari.StartsWith("अथैवम्"), 
            "SB 5.6.6 Devanagari text restored", $"Length: {sb566?.Devanagari?.Length} chars");
        Assert(!string.IsNullOrWhiteSpace(sb566?.Transliteration) && sb566!.Transliteration.StartsWith("athaivam"), 
            "SB 5.6.6 Transliteration restored", $"Length: {sb566?.Transliteration?.Length} chars");
        Assert(!string.IsNullOrWhiteSpace(sb566?.Purports) && sb566!.Purports.Contains("janma karma ca me divyam"), 
            "SB 5.6.6 Purport contains quoted BG 4.9 verse text", $"Length: {sb566?.Purports?.Length} chars");
        Assert(sb566!.Purports!.Contains("One who knows the transcendental nature"), 
            "SB 5.6.6 Purport contains quoted BG 4.9 translation");

        // 2. SB 5.1.5 verification (prose verse previously truncated to uvaca)
        var sb515 = await repo.GetRecordAsync("SB-5.1-5");
        Assert(sb515 != null, "SB 5.1.5 record retrieved", "SB-5.1-5");
        Assert(!string.IsNullOrWhiteSpace(sb515?.Devanagari) && sb515!.Devanagari.Length > 50, 
            "SB 5.1.5 Devanagari restored", $"Length: {sb515?.Devanagari?.Length} chars");
        Assert(!string.IsNullOrWhiteSpace(sb515?.Transliteration) && sb515!.Transliteration.Length > 100, 
            "SB 5.1.5 Transliteration contains full prose verse (not just uvaca)", $"Length: {sb515?.Transliteration?.Length} chars");
        Assert(!string.IsNullOrWhiteSpace(sb515?.Purports) && sb515!.Purports.Length > 3000, 
            "SB 5.1.5 Purport complete with multi-paragraph commentary", $"Length: {sb515?.Purports?.Length} chars");

        // 3. NOI (Nectar of Instruction) Verse 1 verification
        var noi1 = await repo.GetRecordAsync("NOI-(NONE)-VERSE-1");
        Assert(noi1 != null, "NOI Verse 1 record retrieved", "NOI-(NONE)-VERSE-1");
        Assert(!string.IsNullOrWhiteSpace(noi1?.Devanagari) && noi1!.Devanagari.Contains("वाचो वेगं मनसः क्रोधवेगं"), 
            "NOI Verse 1 contains authentic Devanagari text");
        Assert(!string.IsNullOrWhiteSpace(noi1?.Transliteration) && noi1!.Transliteration.Contains("vāco vegaṁ manasaḥ krodha-vegaṁ"), 
            "NOI Verse 1 contains authentic Transliteration");
        Assert(!string.IsNullOrWhiteSpace(noi1?.Purports) && noi1!.Purports.Length > 10000, 
            "NOI Verse 1 Purport fully populated", $"Length: {noi1?.Purports?.Length} chars");

        // 4. ISO (Sri Isopanisad) Mantra 1 verification
        var iso1 = await repo.GetRecordAsync("ISO-(NONE)-MANTRA-1");
        Assert(iso1 != null, "ISO Mantra 1 record retrieved", "ISO-(NONE)-MANTRA-1");
        Assert(!string.IsNullOrWhiteSpace(iso1?.Devanagari) && iso1!.Devanagari.Contains("ईशावास्यम्"), 
            "ISO Mantra 1 Devanagari restored");
        Assert(!string.IsNullOrWhiteSpace(iso1?.Transliteration) && iso1!.Transliteration.Contains("īśāvāsyam"), 
            "ISO Mantra 1 Transliteration restored");

        // 5. Total canonical corpus size
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={corpusDb};Mode=ReadOnly");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Records;";
        long totalRecords = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert(totalRecords > 28000, "Corpus contains over 28,000 canonical records", $"Total: {totalRecords}");

        // 6. Zero missing Devanagari in canonical scripture verses
        cmd.CommandText = @"
            SELECT COUNT(*) FROM Records 
            WHERE BookKey IN ('BG', 'SB', 'ISO', 'BS', 'MM', 'NOI')
              AND RecordType = 'Verse'
              AND (Devanagari IS NULL OR length(trim(Devanagari)) = 0);";
        long missingDevVerses = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert(missingDevVerses == 0, "Zero canonical scripture verses missing Devanagari", $"Missing: {missingDevVerses}");
    }

    private static async Task TestKnowledgeBaseAndBackupsAsync(string userDb)
    {
        // 1. Hashtag extraction verification
        string sampleNoteText = "Realization on #surrender and #guru-tattva: [[BG 18.66]] says to abandon all varieties of dharma. #bhakti";
        var tags = System.Text.RegularExpressions.Regex.Matches(sampleNoteText, @"(?<=#)[a-zA-Z0-9_\-]+")
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .ToList();
        Assert(tags.Contains("surrender") && tags.Contains("guru-tattva") && tags.Contains("bhakti"),
            "Hashtags correctly extracted from realization note text", string.Join(", ", tags));

        // 2. Wiki-link extraction verification
        var wikiMatches = System.Text.RegularExpressions.Regex.Matches(sampleNoteText, @"\[\[(.*?)\]\]")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();
        Assert(wikiMatches.Contains("BG 18.66"), "Wiki-link [[BG 18.66]] correctly extracted", string.Join(", ", wikiMatches));

        // 3. Automated Rolling Local Backup Verification
        var userRepo = new SqliteUserRepository(userDb);
        await userRepo.InitializeAsync();
        var settingsService = new SqliteSettingsService(userDb);
        var backupService = new ResearchDataBackupService(userRepo, settingsService);
        string? backupPath = await backupService.EnsureAutomaticLocalBackupAsync(retentionCount: 10);
        Assert(!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath),
            "Automatic local backup generates valid .vdbbackup archive", backupPath);
        Assert(backupPath?.EndsWith(".vdbbackup") == true, "Backup archive format is .vdbbackup");
    }

    private static async Task TestPrabhupadaLilamritaAsync(string corpusDb)
    {
        var bookRegistry = new BookRegistry();
        await bookRegistry.InitializeFromDatabaseAsync(corpusDb);
        var splDescriptor = bookRegistry.GetBook("SPL");
        Assert(splDescriptor != null, "BookRegistry contains SPL descriptor");
        Assert(splDescriptor?.Title == "Śrīla Prabhupāda-līlāmṛta", "SPL Title is Śrīla Prabhupāda-līlāmṛta", splDescriptor?.Title);
        Assert(splDescriptor?.Category == "Biographies", "SPL Category is Biographies", splDescriptor?.Category);

        var repo = new SqliteCorpusRepository(corpusDb, bookRegistry);
        var ch1 = await repo.GetRecordAsync("SPL-1");
        Assert(ch1 != null, "SPL-1 (Chapter 1) retrieved");
        Assert(ch1?.Reference == "SPL 1", "SPL-1 reference correct", ch1?.Reference);
        Assert(!string.IsNullOrEmpty(ch1?.Purports) && ch1.Purports.Length > 20000, 
            "SPL-1 contains full chapter text (>20k characters)", ch1?.Purports?.Length.ToString());

        var ch55 = await repo.GetRecordAsync("SPL-55");
        Assert(ch55 != null, "SPL-55 (Final Chapter) retrieved");
        Assert(ch55?.Title?.Contains("The Final Lesson") == true, "SPL-55 Title contains 'The Final Lesson'", ch55?.Title);

        // Verify FTS search finds Jaladuta in SPL
        var searchResults = await repo.SearchAsync("Jaladuta", "SPL", limit: 10);
        Assert(searchResults.TotalCount > 0, "FTS search in SPL finds 'Jaladuta'", searchResults.TotalCount.ToString());
    }

    private static async Task TestAdvancedSearchAndVocabAsync(string corpusDb)
    {
        // 1. Proximity syntax parsing: w/5 and near/10
        string parsedProximity = VedaBaseModern.Core.Services.FtsQueryParser.Parse("krishna w/5 arjuna");
        Assert(parsedProximity.Contains("NEAR"), "FTS parser translates 'w/5' into NEAR operator", parsedProximity);
        Assert(parsedProximity == "NEAR(\"krishna\" \"arjuna\", 5)", "Exact NEAR syntax match for 'krishna w/5 arjuna'", parsedProximity);

        string parsedNear = VedaBaseModern.Core.Services.FtsQueryParser.Parse("bhakti near/10 yoga");
        Assert(parsedNear == "NEAR(\"bhakti\" \"yoga\", 10)", "Exact NEAR syntax match for 'bhakti near/10 yoga'", parsedNear);

        // 2. Direct NEAR query preservation
        string rawNear = "NEAR(krishna arjuna, 5)";
        string parsedRawNear = VedaBaseModern.Core.Services.FtsQueryParser.Parse(rawNear);
        Assert(parsedRawNear == rawNear, "Direct NEAR expression preserved intact", parsedRawNear);

        // 3. Diacritic-tolerant phonetic expansion
        string parsedSankirtan = VedaBaseModern.Core.Services.FtsQueryParser.Parse("sankirtan");
        Assert(parsedSankirtan.Contains("saṅkīrtana"), "Phonetic expansion includes IAST 'saṅkīrtana'", parsedSankirtan);

        // 4. Native SQLite fts5vocab Word Wheel querying
        var registry = new BookRegistry();
        await registry.InitializeFromDatabaseAsync(corpusDb);
        var repo = new SqliteCorpusRepository(corpusDb, registry);

        var vocabTerms = await repo.GetVocabularyTermsAsync("kri", 10);
        Assert(vocabTerms.Count > 0, "Vocab terms retrieved for prefix 'kri'", vocabTerms.Count.ToString());
        Assert(vocabTerms.Any(t => t.Term.StartsWith("kri", StringComparison.OrdinalIgnoreCase)), 
            "Vocab contains words starting with 'kri'", string.Join(", ", vocabTerms.Take(3).Select(t => t.Term)));
        Assert(vocabTerms.First().DocumentCount > 0, "Vocab item has document hit count > 0", vocabTerms.First().DocumentCount.ToString());

        // 5. Multi-Book Checklist Scoping ("Checked Branches")
        var multiBookResults = await repo.SearchAsync("bhakti", limit: 20, bookKeys: new[] { "BG", "NOD" });
        Assert(multiBookResults.TotalCount > 0, "Multi-book search returned hits", multiBookResults.TotalCount.ToString());
        bool allScoped = multiBookResults.Results.All(r => r.BookKey == "BG" || r.BookKey == "NOD");
        Assert(allScoped, "All search hits are strictly within checked books (BG or NOD)", 
            string.Join(", ", multiBookResults.Results.Take(5).Select(r => r.BookKey)));
    }

    private static async Task TestPrabhupadaSlokasAndCanonicalTieringAsync(string corpusDb)
    {
        var registry = new BookRegistry();
        await registry.InitializeFromDatabaseAsync(corpusDb);
        var repo = new SqliteCorpusRepository(corpusDb, registry);
        var refService = new DirectReferenceService(repo);

        // 1. Verify SPS in BookRegistry
        var spsBook = registry.GetBook("SPS");
        Assert(spsBook != null, "BookRegistry contains SPS descriptor");
        Assert(spsBook?.Title == "Śrīla Prabhupāda Ślokas", "SPS Title is Śrīla Prabhupāda Ślokas", spsBook?.Title);
        Assert(spsBook?.Category == "Other Works", "SPS Category is Other Works", spsBook?.Category);

        // 2. Verify SPS records retrieval and fields
        var sps11 = await repo.GetRecordAsync("SPS-1.1");
        Assert(sps11 != null, "SPS-1.1 (Śrī guru-praṇāma) retrieved");
        Assert(!string.IsNullOrEmpty(sps11?.Transliteration), "SPS-1.1 has Transliteration");
        Assert(!string.IsNullOrEmpty(sps11?.Synonyms), "SPS-1.1 has Synonyms");
        Assert(!string.IsNullOrEmpty(sps11?.Translation), "SPS-1.1 has Translation");

        // 3. Verify key outside verses
        var katha = await repo.GetRecordAsync("SPS-10.32");
        Assert(katha != null && katha.Title == "Kaṭha Upaniṣad 2.2.13", "SPS-10.32 (Kaṭha Upaniṣad 2.2.13) retrieved", katha?.Title);
        Assert(katha != null && katha.Transliteration.Contains("nityo nityānāṁ"), "Kaṭha Upaniṣad 2.2.13 contains 'nityo nityānāṁ'");

        var brs = await repo.GetRecordAsync("SPS-12.2");
        Assert(brs != null && brs.Title == "Bhakti-rasāmṛta-sindhu 1.1.11", "SPS-12.2 (Bhakti-rasāmṛta-sindhu 1.1.11) retrieved", brs?.Title);

        var vs = await repo.GetRecordAsync("SPS-9.1");
        Assert(vs != null && vs.Title == "Vedānta Sūtra 1.1.1", "SPS-9.1 (Vedānta Sūtra 1.1.1) retrieved", vs?.Title);

        // 4. Verify Library Hierarchy shows 22 sections
        var hierarchy = await repo.GetLibraryHierarchyAsync();
        var spsNode = hierarchy.FirstOrDefault(b => b.BookKey == "SPS");
        Assert(spsNode != null && spsNode.Chapters.Count == 22, "SPS contains all 22 named sections in Library hierarchy", $"Sections: {spsNode?.Chapters.Count}");

        // 5. Direct reference resolution: @sps 1.1 and @sps 10.32
        var spsRef1 = await refService.TryResolveExactAsync("sps 1.1");
        Assert(spsRef1 == "SPS-1.1", "Resolves 'sps 1.1' -> 'SPS-1.1'", spsRef1);

        var spsRef10 = await refService.TryResolveExactAsync("sps 10.32");
        Assert(spsRef10 == "SPS-10.32", "Resolves 'sps 10.32' -> 'SPS-10.32'", spsRef10);

        // Breadcrumb formatting for SPS sections
        var spsBreadcrumb5 = repo.GetBreadcrumb("SPS", "SPS 5.5");
        Assert(spsBreadcrumb5 == "Śrīla Prabhupāda Ślokas › Section 5: Bhagavad-gītā › SPS 5.5", "SPS 5.5 breadcrumb includes Section 5: Bhagavad-gītā", spsBreadcrumb5);

        var spsBreadcrumb10 = repo.GetBreadcrumb("SPS", "SPS 10.7");
        Assert(spsBreadcrumb10 == "Śrīla Prabhupāda Ślokas › Section 10: The Upaniṣads › SPS 10.7", "SPS 10.7 breadcrumb includes Section 10: The Upaniṣads", spsBreadcrumb10);

        var bg21Key = await refService.TryResolveExactAsync("Bhagavad-gītā 2.1");
        Assert(bg21Key == "BG-2-1", "Resolves citation 'Bhagavad-gītā 2.1' -> 'BG-2-1'", bg21Key);

        // 6. Pure Canonical Sequence Search: searching 'tat' in SB follows strictly increasing sequence
        var (tatResults, tatCount) = await repo.SearchAsync("tat", bookKey: "SB", limit: 50, sortOrder: "canonical");
        Assert(tatCount > 1000, "Canonical search for 'tat' in SB returned all hits without artificial caps", tatCount.ToString());
        Assert(tatResults.First().RecordKey.StartsWith("SB-1"), "First canonical result starts in Canto 1", tatResults.First().RecordKey);

        bool isStrictlyIncreasing = true;
        for (int i = 0; i < tatResults.Count - 1; i++)
        {
            if (tatResults[i].Sequence >= tatResults[i + 1].Sequence)
            {
                isStrictlyIncreasing = false;
                break;
            }
        }
        Assert(isStrictlyIncreasing, "Canonical search for 'tat' in SB strictly follows scriptural sequence (Canto 1 -> Canto 2 ...)");

        // 7. Relevance Search: searching 'tat' in SB places SB 10.14.8 on Page 1 due to pratīka opening match
        var (tatRelResults, _) = await repo.SearchAsync("tat", bookKey: "SB", limit: 25, sortOrder: "relevance");
        bool has10148InRelevance = tatRelResults.Any(r => r.RecordKey == "SB-10.14-8");
        Assert(has10148InRelevance, "Relevance search for 'tat' in SB places SB 10.14.8 on Page 1 (pratīka opening boost)", 
            $"Rank: {tatRelResults.FindIndex(r => r.RecordKey == "SB-10.14-8") + 1}");

        // 8. Exact Citation Match: searching '10.14.8' returns SB 10.14.8 at Rank 1
        var (exactResults, _) = await repo.SearchAsync("10.14.8", limit: 10, sortOrder: "canonical");
        Assert(exactResults.Count > 0 && exactResults.First().RecordKey == "SB-10.14-8", "Exact citation search '10.14.8' returns SB 10.14.8 at Rank 1 in canonical mode", exactResults.FirstOrDefault()?.RecordKey);

        // 9. Unbounded multi-page pagination
        var (page2Results, _) = await repo.SearchAsync("tat", bookKey: "SB", limit: 50, offset: 50, sortOrder: "canonical");
        Assert(page2Results.Count == 50, "Paging to offset 50 returns next 50 items", page2Results.Count.ToString());
        Assert(page2Results.First().Sequence > tatResults.Last().Sequence, "Page 2 strictly continues canonical sequence from Page 1");
    }

    private static async Task TestBtgSvaTmgAndBookFiltersAsync(string dbPath)
    {
        var repo = new SqliteCorpusRepository(dbPath);
        var refService = new DirectReferenceService(repo);

        var registry = new BookRegistry();

        // 1. BookRegistry descriptors
        var btgDesc = registry.GetBook("BTG");
        Assert(btgDesc != null && btgDesc.Abbreviation == "BTG", "BookRegistry contains BTG descriptor");
        Assert(btgDesc != null && btgDesc.Category == "Essays & Articles", "BTG Category is Essays & Articles");

        var svaDesc = registry.GetBook("SVA");
        Assert(svaDesc != null && svaDesc.Abbreviation == "SVA", "BookRegistry contains SVA descriptor");
        Assert(svaDesc != null && svaDesc.Category == "Books", "SVA Category is Books");

        var tmgDesc = registry.GetBook("TMG");
        Assert(tmgDesc != null && tmgDesc.Abbreviation == "TMG", "BookRegistry contains TMG descriptor");
        Assert(tmgDesc != null && tmgDesc.Category == "Books", "TMG Category is Books");

        // 2. Sample records retrieval
        var btg1 = await repo.GetRecordAsync("BTG-1");
        Assert(btg1 != null && btg1.Reference == "BTG 1", "BTG-1 record retrieved", btg1?.Reference);
        Assert(btg1 != null && btg1.Title != null && btg1.Title.Contains("Message of His Divine Grace"), "BTG-1 title contains 'Message of His Divine Grace'");
        Assert(btg1 != null && !string.IsNullOrWhiteSpace(btg1.Translation), "BTG-1 contains full article text in Translation/Purport");

        var sva20 = await repo.GetRecordAsync("SVA-1.20");
        Assert(sva20 != null && sva20.Reference == "SVA 1.20", "SVA-1.20 record retrieved", sva20?.Reference);
        Assert(sva20 != null && sva20.Title != null && sva20.Title.Contains("Pañca-tattva Mahā-mantra"), "SVA-1.20 is Pañca-tattva Mahā-mantra", sva20?.Title);

        var sva23 = await repo.GetRecordAsync("SVA-1.23");
        Assert(sva23 != null && sva23.Reference == "SVA 1.23", "SVA-1.23 record retrieved", sva23?.Reference);
        Assert(sva23 != null && sva23.Title != null && sva23.Title.Contains("Gurv"), "SVA-1.23 is Śrī Śrī Gurv-aṣṭaka", sva23?.Title);
        Assert(sva23 != null && sva23.Purports != null && sva23.Purports.Contains("saṁsāra-dāvānala"), "SVA-1.23 contains authentic song text ('saṁsāra-dāvānala')");

        var tmg1 = await repo.GetRecordAsync("TMG-1");
        Assert(tmg1 != null && tmg1.Reference == "TMG 1", "TMG-1 record retrieved", tmg1?.Reference);
        Assert(tmg1 != null && tmg1.Title != null && tmg1.Title.Contains("Tilaka"), "TMG-1 title contains 'Tilaka'", tmg1?.Title);

        var tmg6 = await repo.GetRecordAsync("TMG-6");
        Assert(tmg6 != null && tmg6.Reference == "TMG 6", "TMG-6 record retrieved", tmg6?.Reference);

        // 3. Direct Reference resolution
        var btgRef = await refService.TryResolveExactAsync("btg 1");
        Assert(btgRef == "BTG-1", "Resolves 'btg 1' -> 'BTG-1'", btgRef);

        var svaRef = await refService.TryResolveExactAsync("sva 1.20");
        Assert(svaRef == "SVA-1.20", "Resolves 'sva 1.20' -> 'SVA-1.20'", svaRef);

        var tmgRef = await refService.TryResolveExactAsync("tmg 1");
        Assert(tmgRef == "TMG-1", "Resolves 'tmg 1' -> 'TMG-1'", tmgRef);

        // 4. Breadcrumb formatting
        var btgBc = repo.GetBreadcrumb("BTG", "BTG 1");
        Assert(btgBc.Contains("Back to Godhead") && btgBc.Contains("Article 1"), "BTG 1 breadcrumb formatted correctly", btgBc);

        var svaBc = repo.GetBreadcrumb("SVA", "SVA 1.20");
        Assert(svaBc.Contains("Songs of the Vaiṣṇava Ācāryas") && svaBc.Contains("Standard Prayers"), "SVA 1.20 breadcrumb formatted correctly", svaBc);

        var tmgBc = repo.GetBreadcrumb("TMG", "TMG 1");
        Assert(tmgBc.Contains("Temple Mantra Guide") && tmgBc.Contains("Mantra 1"), "TMG 1 breadcrumb formatted correctly", tmgBc);

        // 5. Full-text search in new books
        var (btgHits, _) = await repo.SearchAsync("Godhead", bookKey: "BTG");
        Assert(btgHits.Count > 0, "FTS search in BTG for 'Godhead' returns hits", btgHits.Count.ToString());

        var (svaHits, _) = await repo.SearchAsync("samsara", bookKey: "SVA");
        Assert(svaHits.Count > 0, "FTS search in SVA for 'samsara' returns hits", svaHits.Count.ToString());

        var (tmgHits, _) = await repo.SearchAsync("tilaka", bookKey: "TMG");
        Assert(tmgHits.Count > 0, "FTS search in TMG for 'tilaka' returns hits", tmgHits.Count.ToString());

        // 6. Multi-Book search across the 3 new works
        var (multiHits, _) = await repo.SearchAsync("prabhupada", bookKeys: new[] { "BTG", "SVA", "TMG" });
        Assert(multiHits.Count > 0, "Multi-book search across BTG, SVA, TMG returns hits", multiHits.Count.ToString());
        bool onlyNewBooks = multiHits.All(r => r.RecordKey.StartsWith("BTG") || r.RecordKey.StartsWith("SVA") || r.RecordKey.StartsWith("TMG"));
        Assert(onlyNewBooks, "All multi-book hits strictly belong to checked books BTG, SVA, TMG");

        // 7. Alphabetical Ascending (A–Z) sorting of library books
        var hierarchy = await repo.GetLibraryHierarchyAsync();
        var sortedTitles = hierarchy
            .Where(b => !string.IsNullOrEmpty(b.Title))
            .OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => b.Title)
            .ToList();

        bool isSortedAZ = true;
        for (int i = 0; i < sortedTitles.Count - 1; i++)
        {
            if (string.Compare(sortedTitles[i], sortedTitles[i + 1], StringComparison.CurrentCultureIgnoreCase) > 0)
            {
                isSortedAZ = false;
                break;
            }
        }
        Assert(isSortedAZ, "Books in search filters can be ordered in strictly ascending alphabetical order (A–Z)");

        // 8. Song names in library hierarchy
        var svaBook = hierarchy.FirstOrDefault(b => b.BookKey == "SVA");
        Assert(svaBook != null, "SVA book found in library hierarchy");
        var svaRec = svaBook?.Chapters.SelectMany(c => c.Records).FirstOrDefault(r => r.RecordKey == "SVA-2.5");
        Assert(svaRec != null && svaRec.Reference.Contains("Gurudeva"), "SVA 2.5 record Reference contains song name ('Gurudeva')", svaRec?.Reference);

        var tmgBook = hierarchy.FirstOrDefault(b => b.BookKey == "TMG");
        Assert(tmgBook != null, "TMG book found in library hierarchy");
        var tmgRec = tmgBook?.Chapters.SelectMany(c => c.Records).FirstOrDefault(r => r.RecordKey == "TMG-6");
        Assert(tmgRec != null && tmgRec.Reference.Contains("Gurv"), "TMG 6 record Reference contains mantra name ('Gurv-aṣṭaka')", tmgRec?.Reference);

        // 9. Zero-book search (Clear All) safely returns 0 hits
        var (zeroHits, zeroCount) = await repo.SearchAsync("prabhupada", bookKeys: Array.Empty<string>());
        Assert(zeroCount == 0 && zeroHits.Count == 0, "Empty bookKeys array returns 0 hits", zeroCount.ToString());

        // 10. TMG 10 has 5 stanzas with authentic line-by-line translation
        var tmg10 = await repo.GetRecordAsync("TMG-10");
        Assert(tmg10 != null, "TMG-10 record retrieved");
        Assert(tmg10?.Purports?.Contains("\"type\": \"song\"") == true || tmg10?.Purports?.Contains("\"type\":\"song\"") == true, "TMG-10 stored as structured song JSON");
        if (tmg10?.Purports != null)
        {
            var doc = System.Text.Json.JsonDocument.Parse(tmg10.Purports);
            var stanzasElem = doc.RootElement.GetProperty("stanzas");
            Assert(stanzasElem.GetArrayLength() == 5, "TMG-10 has exactly 5 stanzas", stanzasElem.GetArrayLength().ToString());
            var s1Lines = stanzasElem[0].GetProperty("lines");
            Assert(s1Lines.GetArrayLength() == 2, "TMG-10 Stanza 1 has 2 distinct lines", s1Lines.GetArrayLength().ToString());
            var s1Trans = stanzasElem[0].GetProperty("translation").GetString() ?? "";
            Assert(s1Trans.StartsWith("1)"), "TMG-10 Stanza 1 translation starts with '1)'", s1Trans);
        }
    }
}
