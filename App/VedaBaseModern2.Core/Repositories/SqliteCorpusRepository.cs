using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Services;

namespace VedaBaseModern.Core.Repositories
{
    public class SqliteCorpusRepository : ICorpusRepository
    {
        private readonly string _connectionString;
        private readonly Registry.IBookRegistry? _bookRegistry;

        public SqliteCorpusRepository(string dbPath, Registry.IBookRegistry? bookRegistry = null)
        {
            _connectionString = $"Data Source={dbPath};Mode=ReadOnly";
            _bookRegistry = bookRegistry;
        }

        public SqliteCorpusRepository(string dbPath) : this(dbPath, null)
        {
        }

        private static readonly Dictionary<string, string> BookTitles = new Dictionary<string, string>
        {
            { "BG", "Bhagavad-gītā As It Is" },
            { "SB", "Śrīmad-Bhāgavatam" },
            { "DI", "Śrī Caitanya-caritāmṛta — Ādi-līlā" },
            { "MADHYA", "Śrī Caitanya-caritāmṛta — Madhya-līlā" },
            { "ANTYA", "Śrī Caitanya-caritāmṛta — Antya-līlā" },
            { "NOD", "The Nectar of Devotion" },
            { "TLC", "Teachings of Lord Caitanya" },
            { "KB", "Kṛṣṇa, the Supreme Personality of Godhead" },
            { "ISO", "Śrī Īśopaniṣad" },
            { "NOI", "The Nectar of Instruction" },
            { "TLK", "Teachings of Lord Kapila" },
            { "TQK", "Teachings of Queen Kuntī" },
            { "BS", "Śrī Brahma-saṁhitā" },
            { "MM", "Mukunda-mālā-stotra" },
            { "NBS", "Nārada-bhakti-sūtra" },
            { "BB", "Bṛhad-bhāgavatāmṛta" },
            { "SPS", "Śrīla Prabhupāda Ślokas" },
            { "DS", "Dialectical Spiritualism" },
            { "BBD", "Beyond Birth and Death" },
            { "POY", "The Perfection of Yoga" },
            { "RV", "Rāja-Vidyā: The King of Knowledge" },
            { "EKC", "Elevation to Kṛṣṇa Consciousness" },
            { "KCTYS", "Kṛṣṇa Consciousness: The Topmost Yoga System" },
            { "MOG", "Message of Godhead" },
            { "LOB", "Light of the Bhāgavata" },
            { "PQPA", "Perfect Questions, Perfect Answers" },
            { "SSR", "The Science of Self-Realization" },
            { "JSD", "The Journey of Self-Discovery" },
            { "LCFL", "Life Comes From Life" },
            { "CB", "Coming Back: The Science of Reincarnation" },
            { "CAT", "Civilization and Transcendence" },
            { "OWK", "On the Way to Kṛṣṇa" },
            { "SFL", "The Search for Liberation" },
            { "TT", "Transcendental Teachings of Prahlāda Mahārāja" },
            { "EJ", "Easy Journey to Other Planets" },
            { "SC", "A Second Chance" },
            { "DWT", "Dharma: The Way of Transcendence" },
            { "POP", "Path of Perfection" },
            { "QFE", "Quest for Enlightenment" },
            { "RTW", "Renunciation Through Wisdom" },
            { "LON", "The Laws of Nature: An Infallible Justice" },
            { "MG", "Matchless Gift" },
            { "ROP", "Reservoir of Pleasure" },
            { "GG", "Gītār Gāna" },
            { "SPL", "Śrīla Prabhupāda-līlāmṛta" },
            { "UNKNOWN", "Life Comes From Life" }
        };

        // The scriptural reading order of works, used both for the Library
        // hierarchy sort and for canonically ordering verse search results
        // (see SearchAsync). Single source of truth - was previously
        // duplicated as a local inside GetLibraryHierarchyAsync.
        internal static readonly List<string> CanonicalBookOrder = new List<string>
        {
            "BG", "SB", "DI", "MADHYA", "ANTYA", "NOD", "TLC", "KB", "ISO", "NOI",
            "TLK", "TQK", "BS", "MM", "NBS", "BB", "SPS", "DS", "BBD", "POY", "RV",
            "EKC", "KCTYS", "MOG", "LOB", "PQPA", "SSR", "JSD", "LCFL", "CB", "CAT",
            "OWK", "SFL", "TT", "EJ", "SC", "DWT", "POP", "QFE",
            "RTW", "LON", "MG", "ROP", "GG", "SPL", "UNKNOWN"
        };

        // CANONICAL EDITION FILTER (Phase 2H finding): 21,406 of the corpus's
        // 50,381 records (42.5%) are duplicate-content occurrences - the same
        // verse/section appearing a second (or third) time elsewhere in the
        // source RTF (e.g. a dated 1972/1974/1975 reprint or lecture-series
        // section, or an anthology reprint of a whole chapter). The extraction
        // pipeline already marks every such duplicate with a '#N' RecordKey
        // suffix (e.g. "BG-1-1#2") and always keeps the FIRST/primary
        // occurrence's key bare (e.g. "BG-1-1") - confirmed by direct
        // inspection: every '#N' key has a corresponding bare key, and the
        // bare key always has the lower Sequence (it was extracted from the
        // verse's original, in-order position; the '#N' duplicate comes from
        // later reprinted/re-dated material further into the file).
        //
        // This is exactly the "older edition duplicating the active book"
        // problem - filtering '#'-suffixed keys out of the Library hierarchy,
        // Search, and adjacent (Previous/Next) navigation is the canonical-
        // edition fix: it does not delete anything (both occurrences remain in
        // the database for historical/provenance completeness - see
        // PHASE_CONTINUATION_REPORT.md), it only stops duplicates from
        // cluttering the active reading/research surfaces. GetRecordAsync/
        // GetRecordsAsync deliberately do NOT apply this filter, so a
        // '#'-suffixed key is still resolvable (never crashes) if something
        // external (a historical bookmark, a direct link) ever references one.
        //
        // A second, smaller residual (59 records) of the SAME reprint-section
        // duplication was found that does NOT get a '#' suffix: grouped/range
        // references (e.g. "Bg 17.8, Bg 17.9, Bg 17.10, Bg 17.8-10, 1972" as
        // the single combined record "BG-17-8-10") never collide with an
        // existing RecordKey, so the pipeline's collision-based '#' suffixing
        // never triggers for them - but they carry the same trailing bare
        // lecture-year marker already independently verified (in the original
        // Book-Metadata Tier 2 work) to occur ONLY in the range 1969-1981 and
        // ONLY as this reprint/lecture-citation artifact. The extra clause
        // below is deliberately scoped to only the verse-numbered scripture
        // books (BG/SB/CC/Bs) where that pattern is established, specifically
        // to avoid a false positive found during testing: an SSR essay whose
        // own title genuinely contains a real date ("...Los Angeles Times,
        // January 14, 1970") is prose, not a duplicate verse, and must not be
        // filtered.
        //
        // Takes an optional table alias prefix (e.g. "r") because SearchAsync
        // joins RecordsFts (which ALSO has a same-named Reference column) with
        // Records - an unqualified "Reference" there would be ambiguous.
        internal static string ExcludeDuplicateContentSql(string tableAlias = "")
        {
            string p = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
            return $"{p}RecordKey NOT LIKE '%#%' AND NOT ({p}BookKey IN ('BG','SB','DI','MADHYA','ANTYA','BS') AND ({p}Reference GLOB '*, 19[6-8][0-9]' OR {p}Reference GLOB '*-19[6-8][0-9]'))";
        }

        // The full 17-book/chapter/record hierarchy is expensive to build (one
        // pass grouping all 50,166 records) but can never change - the corpus is
        // frozen and read-only for the life of the process. Both MainViewModel
        // (NavigationView) and SearchViewModel (the book filter) call this
        // independently; without caching, opening Search re-computed the whole
        // hierarchy every single time. This caches only the lightweight
        // BookNode/ChapterNode/RecordNode graph (RecordKey/Reference/Sequence
        // strings - never Sanskrit/translation/purport text), not corpus record
        // content, so it is not the "global corpus cache" the milestone warns
        // against.
        private List<BookNode>? _cachedHierarchy;

        public void InvalidateLibraryHierarchyCache() => _cachedHierarchy = null;

        public async Task<List<BookNode>> GetLibraryHierarchyAsync()
        {
            if (_cachedHierarchy != null) return _cachedHierarchy;

            var books = new List<BookNode>();

            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // Books table metadata (title / canonical order / pdf), when present.
                // Falls back to the static BookTitles/CanonicalBookOrder for any
                // key the table doesn't have an entry for.
                var dbTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var dbOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var dbAuthors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var dbCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var dbIsPdf = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var dbPdfPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var metaCmd = connection.CreateCommand();
                    metaCmd.CommandText = "SELECT BookKey, Title, CanonicalOrder, Author, Category, IsPdf, PdfPath FROM Books;";
                    using var metaReader = metaCmd.ExecuteReader();
                    while (metaReader.Read())
                    {
                        string bk = metaReader.GetString(0);
                        if (!metaReader.IsDBNull(1))
                        {
                            string t = metaReader.GetString(1);
                            if (!string.IsNullOrWhiteSpace(t)) dbTitles[bk] = t;
                        }
                        if (!metaReader.IsDBNull(2)) dbOrder[bk] = metaReader.GetInt32(2);
                        if (!metaReader.IsDBNull(3))
                        {
                            string a = metaReader.GetString(3);
                            if (!string.IsNullOrWhiteSpace(a)) dbAuthors[bk] = a;
                        }
                        if (!metaReader.IsDBNull(4))
                        {
                            string c = metaReader.GetString(4);
                            if (!string.IsNullOrWhiteSpace(c)) dbCategories[bk] = c;
                        }
                        if (metaReader.FieldCount > 5 && !metaReader.IsDBNull(5))
                        {
                            dbIsPdf[bk] = metaReader.GetInt32(5) != 0;
                        }
                        if (metaReader.FieldCount > 6 && !metaReader.IsDBNull(6))
                        {
                            string p = metaReader.GetString(6);
                            if (!string.IsNullOrWhiteSpace(p)) dbPdfPaths[bk] = p;
                        }
                    }
                }
                catch
                {
                    // Fallback for older schema without IsPdf/PdfPath
                    try
                    {
                        using var fallbackCmd = connection.CreateCommand();
                        fallbackCmd.CommandText = "SELECT BookKey, Title, CanonicalOrder, Author, Category FROM Books;";
                        using var metaReader = fallbackCmd.ExecuteReader();
                        while (metaReader.Read())
                        {
                            string bk = metaReader.GetString(0);
                            if (!metaReader.IsDBNull(1)) dbTitles[bk] = metaReader.GetString(1);
                            if (!metaReader.IsDBNull(2)) dbOrder[bk] = metaReader.GetInt32(2);
                            if (!metaReader.IsDBNull(3)) dbAuthors[bk] = metaReader.GetString(3);
                            if (!metaReader.IsDBNull(4)) dbCategories[bk] = metaReader.GetString(4);
                        }
                    }
                    catch { }
                }
                // Books table optional / older schema - static fallbacks below cover it.

                // Get all BookKeys ordered
                using var bookCmd = connection.CreateCommand();
                bookCmd.CommandText = "SELECT DISTINCT BookKey FROM Records";
                var bookKeys = new List<string>();
                using (var bookReader = bookCmd.ExecuteReader())
                {
                    while (bookReader.Read())
                    {
                        bookKeys.Add(bookReader.GetString(0));
                    }
                }

                // Sort books (DB CanonicalOrder, else static CanonicalBookOrder,
                // then alphabetical, UNKNOWN last)
                bookKeys.Sort((a, b) =>
                {
                    if (a == "UNKNOWN" && b == "UNKNOWN") return 0;
                    if (a == "UNKNOWN") return 1;
                    if (b == "UNKNOWN") return -1;

                    int indexA = dbOrder.TryGetValue(a, out var oa) ? oa : CanonicalBookOrder.IndexOf(a);
                    int indexB = dbOrder.TryGetValue(b, out var ob) ? ob : CanonicalBookOrder.IndexOf(b);

                    if (indexA != -1 && indexB != -1) return indexA.CompareTo(indexB);
                    if (indexA != -1) return -1;
                    if (indexB != -1) return 1;

                    return a.CompareTo(b);
                });

                var bookMap = new Dictionary<string, BookNode>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in bookKeys)
                {
                    string title = dbTitles.TryGetValue(key, out var dbT) ? dbT : GetBookTitle(key);
                    string author = dbAuthors.TryGetValue(key, out var dbA) ? dbA : "His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda";
                    string category = dbCategories.TryGetValue(key, out var dbC) ? dbC : "";
                    dbIsPdf.TryGetValue(key, out bool isPdf);
                    dbPdfPaths.TryGetValue(key, out string? pdfPath);
                    var bookNode = new BookNode
                    {
                        BookKey = key,
                        Title = title,
                        Author = author,
                        Category = category,
                        IsPdf = isPdf,
                        PdfPath = pdfPath
                    };
                    books.Add(bookNode);
                    bookMap[key] = bookNode;
                }

                // Single streaming pass over every book's records instead of one
                // query per book (was up to 33+ sequential round-trips).
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT BookKey, RecordKey, Reference, Title, Sequence FROM Records WHERE {ExcludeDuplicateContentSql()} ORDER BY BookKey, Sequence";

                using var reader = cmd.ExecuteReader();
                var chapterMaps = new Dictionary<string, Dictionary<string, ChapterNode>>(StringComparer.OrdinalIgnoreCase);

                while (reader.Read())
                {
                    string bk = reader.GetString(0);
                    if (!bookMap.TryGetValue(bk, out var bookNode)) continue;

                    string rk = reader.GetString(1);
                    if (bk == "SPS" && rk.StartsWith("SPS-SEC-", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string refText = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    string? recordTitle = reader.IsDBNull(3) ? null : reader.GetString(3);
                    int seq = reader.GetInt32(4);

                    string chapterTitle = DeriveChapterTitle(bk, refText, recordTitle);

                    if (!chapterMaps.TryGetValue(bk, out var chapterMap))
                    {
                        chapterMap = new Dictionary<string, ChapterNode>(StringComparer.OrdinalIgnoreCase);
                        chapterMaps[bk] = chapterMap;
                    }

                    if (!chapterMap.TryGetValue(chapterTitle, out var chNode))
                    {
                        chNode = new ChapterNode { Title = chapterTitle };
                        chapterMap[chapterTitle] = chNode;
                        bookNode.Chapters.Add(chNode);
                    }

                    string displayRef = string.IsNullOrWhiteSpace(refText) ? rk : refText;
                    if (bk == "SPS" && !string.IsNullOrWhiteSpace(recordTitle))
                    {
                        displayRef = $"{displayRef}: {recordTitle}";
                    }

                    chNode.Records.Add(new RecordNode
                    {
                        RecordKey = rk,
                        Reference = displayRef,
                        Sequence = seq
                    });
                }
            });

            _cachedHierarchy = books;
            return books;
        }

        private static readonly System.Text.RegularExpressions.Regex ProseNumberedChapter =
            new(@"^[A-Za-z0-9]+\s+(\d+):\s*(.*)$");
        private static readonly System.Text.RegularExpressions.Regex ProseFrontMatter =
            new(@"^[A-Za-z0-9]+(?::\s*|\s+)(Introduction|Preface|Foreword|Dedication|Words from Apple|Conclusion|Epilogue|Prologue)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex ProseGgChapter =
            new(@"^GG:?\s*Chapter\s+(\d+)$|^GG\s+(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex ProseAnyChapterNumber =
            new(@"^[A-Za-z0-9]+\s+(\d+)");

        private string DeriveChapterTitle(string bookKey, string reference, string? recordTitle = null)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return string.IsNullOrWhiteSpace(recordTitle) ? "Verses" : recordTitle!;
            }

            // Always work off the first reference in case of multi-verse records like "Bg 1.16, Bg 1.17"
            string firstRef = reference.Split(',')[0].Trim();

            // Front-matter sections (Setting the Scene, Dedication, Preface,
            // Introduction, Foreword) - checked before any book-specific
            // chapter-number parsing so they resolve to their own clean
            // section title (e.g. Reference "Bg Dedication" -> "Dedication")
            // regardless of which book they belong to, and sort to the top
            // of the chapter list via Sequence rather than title grouping.
            var earlyFrontMatterMatch = System.Text.RegularExpressions.Regex.Match(
                firstRef, @"(Setting the Scene|Dedication|Preface|Introduction|Foreword)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (earlyFrontMatterMatch.Success)
            {
                return earlyFrontMatterMatch.Groups[1].Value;
            }

            if (bookKey == "BG")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^Bg\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    int n = int.Parse(match.Groups[1].Value);
                    string? title = Metadata.CanonicalChapterTitles.GetBgTitle(n);
                    return title != null ? $"Chapter {n}: {title}" : $"Chapter {n}";
                }
            }
            else if (bookKey == "SB")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^(?:SB\s+)?(\d+)\.(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    int canto = int.Parse(match.Groups[1].Value);
                    int ch = int.Parse(match.Groups[2].Value);
                    string? title = Metadata.CanonicalChapterTitles.GetSbTitle(canto, ch);
                    return title != null ? $"Canto {canto} Chapter {ch}: {title}" : $"Canto {canto} Chapter {ch}";
                }
            }
            else if (bookKey == "DI" || bookKey == "MADHYA" || bookKey == "ANTYA")
            {
                if (firstRef.Contains("Concluding Words", StringComparison.OrdinalIgnoreCase))
                {
                    return "Concluding Words";
                }
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^(?:[ĀA]di|Madhya|Antya)\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    int n = int.Parse(match.Groups[1].Value);
                    string? title = Metadata.CanonicalChapterTitles.GetCcTitle(bookKey, n);
                    return title != null ? $"Chapter {n}: {title}" : $"Chapter {n}";
                }
            }
            else if (bookKey == "BS")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^Bs\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"Chapter {match.Groups[1].Value}";
                return "Chapter 5";
            }
            else if (bookKey == "BB")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^BB\s+(\d+)\.(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"Part {match.Groups[1].Value} Chapter {match.Groups[2].Value}";
            }
            else if (bookKey == "ISO")
            {
                return "Mantras";
            }
            else if (bookKey == "NOI")
            {
                return "Texts 1–11";
            }
            else if (bookKey == "MM")
            {
                return "Verses";
            }
            else if (bookKey == "UNKNOWN")
            {
                return "Morning Walk Conversations";
            }
            else if (bookKey == "SPS")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^SPS\s+(?:Section\s+)?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    int sec = int.Parse(match.Groups[1].Value);
                    return sec switch
                    {
                        1 => "1. Auspicious Invocation Mantras",
                        2 => "2. Śrī Śrī Gurv-aṣṭaka",
                        3 => "3. Śrī Śrī Ṣaḍ-gosvāmy-aṣṭaka",
                        4 => "4. Śrī Śrī Śikṣāṣṭaka",
                        5 => "5. Bhagavad-gītā",
                        6 => "6. Śrīmad-Bhāgavatam",
                        7 => "7. Caitanya-caritāmṛta",
                        8 => "8. Śrī Brahma-saṁhitā",
                        9 => "9. Vedānta-sūtra",
                        10 => "10. The Upaniṣads",
                        11 => "11. Caitanya Bhāgavata",
                        12 => "12. Six Gosvāmīs & Others",
                        13 => "13. Purāṇas",
                        14 => "14. Mahābhārata",
                        15 => "15. Other Vedic Literatures",
                        16 => "16. Previous Ācāryas",
                        17 => "17. Bhaktivinoda Ṭhākura",
                        18 => "18. Narottama dāsa Ṭhākura",
                        19 => "19. Jayadeva Gosvāmī",
                        20 => "20. Nīti-śāstra",
                        21 => "21. Non Devotees",
                        22 => "22. Quotes from Other Sources",
                        _ => $"Section {sec}"
                    };
                }
                return "Verses";
            }

            // Prose/anthology books (KB, NOD, TLC, BBD, CAT, DS, DWT, EJ, EKC,
            // GG, JSD, KCTYS, LOB, LON, MG, MOG, NBS, OWK, POP, POY, PQPA, QFE,
            // RTW, RV, SC, SSR, TLK, TQK, and any other book not special-cased
            // above): each record IS its own chapter/section, so the derived
            // title must be unique per record rather than bucketing everything
            // into one generic "Verses" chapter.
            var ggMatch = ProseGgChapter.Match(firstRef);
            if (ggMatch.Success)
            {
                string ggNum = ggMatch.Groups[1].Success ? ggMatch.Groups[1].Value : ggMatch.Groups[2].Value;
                return $"Chapter {ggNum}";
            }

            var numberedMatch = ProseNumberedChapter.Match(firstRef);
            if (numberedMatch.Success)
            {
                return $"Chapter {numberedMatch.Groups[1].Value}: {numberedMatch.Groups[2].Value}";
            }

            var frontMatterMatch = ProseFrontMatter.Match(firstRef);
            if (frontMatterMatch.Success)
            {
                return frontMatterMatch.Groups[1].Value;
            }

            if (!string.IsNullOrWhiteSpace(recordTitle))
            {
                var chapterNumMatch = ProseAnyChapterNumber.Match(firstRef);
                return chapterNumMatch.Success
                    ? $"Chapter {chapterNumMatch.Groups[1].Value}: {recordTitle}"
                    : recordTitle!;
            }

            return "Verses";
        }

        public async Task<CorpusRecord?> GetRecordAsync(string recordKey)
        {
            CorpusRecord? record = null;
            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT 
                        RecordKey, BookKey, Sequence, ParentKey, RecordType, Reference, ReferenceStatus, Title,
                        Devanagari, Transliteration, Synonyms, Translation, Purports
                    FROM Records 
                    WHERE RecordKey = $rk";
                cmd.Parameters.AddWithValue("$rk", recordKey);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    record = new CorpusRecord
                    {
                        RecordKey = reader.GetString(0),
                        BookKey = reader.GetString(1),
                        Sequence = reader.GetInt32(2),
                        ParentKey = reader.IsDBNull(3) ? null : reader.GetString(3),
                        RecordType = reader.GetString(4),
                        Reference = reader.IsDBNull(5) ? null : reader.GetString(5),
                        ReferenceStatus = reader.GetString(6),
                        Title = reader.IsDBNull(7) ? null : reader.GetString(7),
                        Devanagari = reader.IsDBNull(8) ? "" : reader.GetString(8),
                        Transliteration = reader.IsDBNull(9) ? "" : reader.GetString(9),
                        Synonyms = reader.IsDBNull(10) ? "" : reader.GetString(10),
                        Translation = reader.IsDBNull(11) ? "" : reader.GetString(11),
                        Purports = reader.IsDBNull(12) ? "" : reader.GetString(12)
                    };
                }
            });

            return record;
        }

        public string GetBookTitle(string bookKey)
        {
            string? fromRegistry = _bookRegistry?.GetBookTitle(bookKey);
            if (!string.IsNullOrEmpty(fromRegistry) && !string.Equals(fromRegistry, bookKey, StringComparison.OrdinalIgnoreCase))
            {
                return fromRegistry;
            }
            return BookTitles.TryGetValue(bookKey, out var t) ? t : bookKey;
        }

        public string GetCanonicalChapterHeader(string bookKey, string? reference)
        {
            string bookTitle = GetBookTitle(bookKey);
            string chapterTitle = DeriveChapterTitle(bookKey, reference ?? "");
            return $"{bookTitle} — {chapterTitle}";
        }

        public string GetBreadcrumb(string bookKey, string? reference)
        {
            string bookTitle = GetBookTitle(bookKey);
            if (string.IsNullOrWhiteSpace(reference)) return bookTitle;

            string firstRef = reference.Split(',')[0].Trim();

            if (bookKey == "BG")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^Bg\s+(\d+)\.(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"{bookTitle} › Chapter {match.Groups[1].Value} › Verse {match.Groups[2].Value}";
                var chMatch = System.Text.RegularExpressions.Regex.Match(firstRef, @"^Bg\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (chMatch.Success) return $"{bookTitle} › Chapter {chMatch.Groups[1].Value}";
            }
            else if (bookKey == "SB")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^(?:SB\s+)?(\d+)\.(\d+)\.(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"{bookTitle} › Canto {match.Groups[1].Value} › Chapter {match.Groups[2].Value} › Verse {match.Groups[3].Value}";
                var chMatch = System.Text.RegularExpressions.Regex.Match(firstRef, @"^(?:SB\s+)?(\d+)\.(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (chMatch.Success) return $"{bookTitle} › Canto {chMatch.Groups[1].Value} › Chapter {chMatch.Groups[2].Value}";
            }
            else if (bookKey == "DI" || bookKey == "MADHYA" || bookKey == "ANTYA")
            {
                string lila = bookKey == "DI" ? "Ādi-līlā" : (bookKey == "MADHYA" ? "Madhya-līlā" : "Antya-līlā");
                if (firstRef.Contains("Concluding Words", StringComparison.OrdinalIgnoreCase))
                    return $"Śrī Caitanya-caritāmṛta › {lila} › Concluding Words";
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^(?:[ĀA]di|Madhya|Antya)\s+(\d+)\.(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"Śrī Caitanya-caritāmṛta › {lila} › Chapter {match.Groups[1].Value} › Verse {match.Groups[2].Value}";
                var chMatch = System.Text.RegularExpressions.Regex.Match(firstRef, @"^(?:[ĀA]di|Madhya|Antya)\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (chMatch.Success) return $"Śrī Caitanya-caritāmṛta › {lila} › Chapter {chMatch.Groups[1].Value}";
            }
            else if (bookKey == "ISO")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"{bookTitle} › Mantra {match.Groups[1].Value}";
            }
            else if (bookKey == "NOI")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"{bookTitle} › Verse {match.Groups[1].Value}";
            }
            else if (bookKey == "BS")
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstRef, @"^Bs\s+(\d+)\.(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return $"{bookTitle} › Chapter {match.Groups[1].Value} › Verse {match.Groups[2].Value}";
            }

            return $"{bookTitle} › {firstRef}";
        }

        public async Task<List<CorpusRecord>> GetRecordsAsync(IEnumerable<string> recordKeys)
        {
            var keys = recordKeys.Distinct().ToList();
            var records = new List<CorpusRecord>();
            if (keys.Count == 0) return records;

            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                var placeholders = new List<string>();
                for (int i = 0; i < keys.Count; i++)
                {
                    string p = $"$k{i}";
                    placeholders.Add(p);
                    cmd.Parameters.AddWithValue(p, keys[i]);
                }
                cmd.CommandText = $@"
                    SELECT
                        RecordKey, BookKey, Sequence, ParentKey, RecordType, Reference, ReferenceStatus, Title,
                        Devanagari, Transliteration, Synonyms, Translation, Purports
                    FROM Records
                    WHERE RecordKey IN ({string.Join(",", placeholders)})";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    records.Add(new CorpusRecord
                    {
                        RecordKey = reader.GetString(0),
                        BookKey = reader.GetString(1),
                        Sequence = reader.GetInt32(2),
                        ParentKey = reader.IsDBNull(3) ? null : reader.GetString(3),
                        RecordType = reader.GetString(4),
                        Reference = reader.IsDBNull(5) ? null : reader.GetString(5),
                        ReferenceStatus = reader.GetString(6),
                        Title = reader.IsDBNull(7) ? null : reader.GetString(7),
                        Devanagari = reader.IsDBNull(8) ? "" : reader.GetString(8),
                        Transliteration = reader.IsDBNull(9) ? "" : reader.GetString(9),
                        Synonyms = reader.IsDBNull(10) ? "" : reader.GetString(10),
                        Translation = reader.IsDBNull(11) ? "" : reader.GetString(11),
                        Purports = reader.IsDBNull(12) ? "" : reader.GetString(12)
                    });
                }
            });

            return records;
        }

        public async Task<string?> GetAdjacentRecordKeyAsync(string currentRecordKey, bool next)
        {
            string? adjacentKey = null;
            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // First get the current BookKey and Sequence
                using var cmd1 = connection.CreateCommand();
                cmd1.CommandText = "SELECT BookKey, Sequence FROM Records WHERE RecordKey = $rk";
                cmd1.Parameters.AddWithValue("$rk", currentRecordKey);
                
                string bookKey = "";
                int seq = -1;
                using (var reader = cmd1.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        bookKey = reader.GetString(0);
                        seq = reader.GetInt32(1);
                    }
                }

                if (seq == -1) return;

                // Find the next or previous record within the same book, among
                // canonical (non-duplicate) records only - see
                // ExcludeDuplicateContentSql. If the CURRENT record itself is a
                // '#'-suffixed duplicate (reachable only via an old external
                // reference, never via normal navigation), Previous/Next still
                // correctly steps through the canonical sequence around it.
                using var cmd2 = connection.CreateCommand();
                if (next)
                {
                    cmd2.CommandText = $"SELECT RecordKey FROM Records WHERE BookKey = $bk AND Sequence > $seq AND {ExcludeDuplicateContentSql()} ORDER BY Sequence ASC LIMIT 1";
                }
                else
                {
                    cmd2.CommandText = $"SELECT RecordKey FROM Records WHERE BookKey = $bk AND Sequence < $seq AND {ExcludeDuplicateContentSql()} ORDER BY Sequence DESC LIMIT 1";
                }
                
                cmd2.Parameters.AddWithValue("$bk", bookKey);
                cmd2.Parameters.AddWithValue("$seq", seq);

                var result = cmd2.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    adjacentKey = (string)result;
                }
            });

            return adjacentKey;
        }

        public async Task<List<CorpusRecord>> GetChapterRecordsAsync(string recordKey)
        {
            var target = await GetRecordAsync(recordKey);
            if (target == null) return new List<CorpusRecord>();

            string bookKey = target.BookKey;
            string chapterTitle = DeriveChapterTitle(bookKey, target.Reference ?? "");

            var hierarchy = await GetLibraryHierarchyAsync();
            var book = hierarchy.FirstOrDefault(b => b.BookKey == bookKey);
            if (book == null) return new List<CorpusRecord> { target };

            var chapter = book.Chapters.FirstOrDefault(c => c.Title == chapterTitle);
            if (chapter == null) return new List<CorpusRecord> { target };

            var recordKeys = chapter.Records.Select(r => r.RecordKey).ToList();
            var records = await GetRecordsAsync(recordKeys);

            var keyToIndex = recordKeys.Select((k, idx) => (k, idx)).ToDictionary(x => x.k, x => x.idx);
            return records.OrderBy(r => keyToIndex.TryGetValue(r.RecordKey, out var idx) ? idx : r.Sequence).ToList();
        }

        private int? _ftsColumnCount;

        private int GetFtsColumnCount(SqliteConnection connection)
        {
            if (_ftsColumnCount.HasValue) return _ftsColumnCount.Value;
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(RecordsFts)";
            using var reader = cmd.ExecuteReader();
            int count = 0;
            while (reader.Read()) count++;
            _ftsColumnCount = count;
            return count;
        }

        // How many top-BM25-ranked candidates to pull back before canonically
        // re-sorting (see below). This is a deliberate, bounded tradeoff, not
        // "unlimited": the overwhelming majority of research queries match far
        // fewer than this many verses, and for those the canonical order below
        // is exact and complete. Only a handful of extremely common single
        // words (e.g. "krishna", ~14,146 matches per Milestone 3's own
        // validation numbers) exceed this cap - for those, the displayed

        public async Task<List<VocabTerm>> GetVocabularyTermsAsync(string prefix, int limit = 60)
        {
            var list = new List<VocabTerm>();
            string cleanPrefix = (prefix ?? "").Trim().ToLowerInvariant();

            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using (var initCmd = connection.CreateCommand())
                {
                    initCmd.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS RecordsFts_vocab USING fts5vocab('RecordsFts', 'row');";
                    initCmd.ExecuteNonQuery();
                }

                if (string.IsNullOrEmpty(cleanPrefix))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT term, doc, cnt FROM RecordsFts_vocab ORDER BY term LIMIT @limit";
                    cmd.Parameters.AddWithValue("@limit", limit);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new VocabTerm(
                            reader.GetString(0),
                            reader.GetInt32(1),
                            reader.GetInt32(2)
                        ));
                    }
                }
                else
                {
                    // 1. Fetch 5 preceding words alphabetically
                    var preceding = new List<VocabTerm>();
                    using (var preCmd = connection.CreateCommand())
                    {
                        preCmd.CommandText = "SELECT term, doc, cnt FROM RecordsFts_vocab WHERE term < @prefix ORDER BY term DESC LIMIT 5";
                        preCmd.Parameters.AddWithValue("@prefix", cleanPrefix);
                        using var preReader = preCmd.ExecuteReader();
                        while (preReader.Read())
                        {
                            preceding.Add(new VocabTerm(
                                preReader.GetString(0),
                                preReader.GetInt32(1),
                                preReader.GetInt32(2)
                            ));
                        }
                    }
                    preceding.Reverse();
                    list.AddRange(preceding);

                    // 2. Fetch succeeding words (starting from prefix)
                    using (var postCmd = connection.CreateCommand())
                    {
                        postCmd.CommandText = "SELECT term, doc, cnt FROM RecordsFts_vocab WHERE term >= @prefix ORDER BY term ASC LIMIT @limit";
                        postCmd.Parameters.AddWithValue("@prefix", cleanPrefix);
                        postCmd.Parameters.AddWithValue("@limit", Math.Max(20, limit - preceding.Count));
                        using var postReader = postCmd.ExecuteReader();
                        while (postReader.Read())
                        {
                            list.Add(new VocabTerm(
                                postReader.GetString(0),
                                postReader.GetInt32(1),
                                postReader.GetInt32(2)
                            ));
                        }
                    }
                }
            });

            return list;
        }

        public async Task<(List<SearchResult> Results, int TotalCount)> SearchAsync(string query, string? bookKey = null, int limit = 50, int offset = 0, string? fieldScope = null, IEnumerable<string>? bookKeys = null, bool isExactWord = false, string sortOrder = "relevance", bool isExactCase = false)
        {
            var results = new List<SearchResult>();
            int totalCount = 0;

            if (string.IsNullOrWhiteSpace(query))
                return (results, totalCount);

            string ftsQuery = Services.FtsQueryParser.Parse(query, isExactWord);
            if (string.IsNullOrWhiteSpace(ftsQuery))
                return (results, totalCount);

            if (!string.IsNullOrWhiteSpace(fieldScope) && !fieldScope.Equals("All", StringComparison.OrdinalIgnoreCase) && !fieldScope.Equals("All Fields", StringComparison.OrdinalIgnoreCase))
            {
                string targetCol = fieldScope.Trim().ToLowerInvariant() switch
                {
                    "purports" or "purports only" or "purport" => "Purports",
                    "translation" or "translations" or "translations only" => "Translation",
                    "synonyms" or "synonym" or "synonyms only" => "Synonyms",
                    "devanagari" or "devanagari only" => "Devanagari",
                    "verse / transliteration only" or "transliteration only" or "transliteration" => "Transliteration",
                    "verse & synonyms" => "{Transliteration Synonyms}",
                    _ => fieldScope.Trim()
                };
                ftsQuery = $"{targetCol} : ({ftsQuery})";
            }

            var multiKeys = bookKeys != null ? bookKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList() : new List<string>();
            var expandedMultiKeys = new List<string>();
            foreach (var bk in multiKeys)
            {
                if (bk == "CC")
                {
                    expandedMultiKeys.Add("DI");
                    expandedMultiKeys.Add("MADHYA");
                    expandedMultiKeys.Add("ANTYA");
                }
                else
                {
                    expandedMultiKeys.Add(bk);
                }
            }

            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // Read-only performance pragmas (Phase 4.5): let SQLite memory-map
                // the corpus file and keep a larger page cache, so repeated FTS5
                // queries against the same read-only 50k-record database hit
                // memory instead of re-reading from disk on every call. Pure
                // runtime/session tuning - never persisted, never touches the
                // database file itself, and changes no query result or ordering.
                using (var pragmaCmd = connection.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA mmap_size = 268435456; PRAGMA cache_size = -64000;";
                    pragmaCmd.ExecuteNonQuery();
                }

                try
                {
                    int colCount = GetFtsColumnCount(connection);
                    string rankExpression = colCount >= 8
                        ? "bm25(RecordsFts, 25.0, 25.0, 5.0, 4.0, 15.0, 10.0, 5.0, 1.0)"
                        : (colCount == 6 ? "bm25(RecordsFts, 5.0, 4.0, 4.0, 2.0, 5.0, 1.0)" : "rank");

                    // 1. Get total count (canonical, non-duplicate records only -
                    // see ExcludeDuplicateContentSql / Phase 2H canonical-edition finding)
                    using var countCmd = connection.CreateCommand();
                    string countSql = $"SELECT COUNT(*) FROM RecordsFts fts JOIN Records r ON r.rowid = fts.rowid WHERE RecordsFts MATCH $query AND {ExcludeDuplicateContentSql("r")}";
                    if (expandedMultiKeys.Count > 0)
                    {
                        var placeholders = new List<string>();
                        for (int i = 0; i < expandedMultiKeys.Count; i++)
                        {
                            string pName = $"$bkM_{i}";
                            placeholders.Add(pName);
                            countCmd.Parameters.AddWithValue(pName, expandedMultiKeys[i]);
                        }
                        countSql += $" AND r.BookKey IN ({string.Join(", ", placeholders)})";
                    }
                    else if (!string.IsNullOrEmpty(bookKey))
                    {
                        if (bookKey == "CC")
                        {
                            countSql += " AND r.BookKey IN ('DI', 'MADHYA', 'ANTYA')";
                        }
                        else
                        {
                            countSql += " AND r.BookKey = $bk";
                            countCmd.Parameters.AddWithValue("$bk", bookKey);
                        }
                    }

                    if (isExactCase)
                    {
                        countSql += IastSearchHelper.BuildCaseGlobSqlClause(query, countCmd, "cCase");
                    }

                    countCmd.CommandText = countSql;
                    countCmd.Parameters.AddWithValue("$query", ftsQuery);
                    totalCount = Convert.ToInt32(countCmd.ExecuteScalar());

                    if (totalCount == 0) return;

                    // 2. Fetch results with direct database pagination and deterministic ordering.
                    // Canonical mode sorts strictly by canonical book order and scriptural sequence
                    // (with exact citation match prioritized if query is a verse reference).
                    // Relevance mode sorts by citation match, pratika verse openings, and BM25 rank.
                    string leadWord = query.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? query.Trim();
                    leadWord = System.Text.RegularExpressions.Regex.Replace(leadWord, @"[^\p{L}\p{N}\-]", "");

                    using var queryCmd = connection.CreateCommand();
                    string querySql = $@"
                        SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence,
                               (CASE 
                                  WHEN r.Reference = $cleanQuery OR r.RecordKey = $cleanQuery THEN 1
                                  WHEN r.Reference LIKE $cleanQuery || '%' OR r.Reference LIKE '% ' || $cleanQuery || '%' THEN 2
                                  WHEN r.RecordKey LIKE '%' || $cleanQuery || '%' THEN 3
                                  ELSE 4
                                END) AS ExactCitationPriority
                        FROM RecordsFts fts
                        JOIN Records r ON r.rowid = fts.rowid
                        LEFT JOIN Books b ON b.BookKey = r.BookKey
                        WHERE RecordsFts MATCH $query AND {ExcludeDuplicateContentSql("r")} ";

                    if (expandedMultiKeys.Count > 0)
                    {
                        var placeholders = new List<string>();
                        for (int i = 0; i < expandedMultiKeys.Count; i++)
                        {
                            string pName = $"$bkM_{i}";
                            placeholders.Add(pName);
                            queryCmd.Parameters.AddWithValue(pName, expandedMultiKeys[i]);
                        }
                        querySql += $" AND r.BookKey IN ({string.Join(", ", placeholders)}) ";
                    }
                    else if (!string.IsNullOrEmpty(bookKey))
                    {
                        if (bookKey == "CC")
                        {
                            querySql += " AND r.BookKey IN ('DI', 'MADHYA', 'ANTYA') ";
                        }
                        else
                        {
                            querySql += " AND r.BookKey = $bk ";
                            queryCmd.Parameters.AddWithValue("$bk", bookKey);
                        }
                    }

                    if (isExactCase)
                    {
                        querySql += IastSearchHelper.BuildCaseGlobSqlClause(query, queryCmd, "qCase");
                    }

                    string orderExpression;
                    if (sortOrder.Equals("canonical", StringComparison.OrdinalIgnoreCase))
                    {
                        orderExpression = "ExactCitationPriority, COALESCE(b.CanonicalOrder, 9999), r.Sequence";
                    }
                    else
                    {
                        orderExpression = $@"
                            (CASE 
                                WHEN r.Reference = $cleanQuery OR r.RecordKey = $cleanQuery THEN -2500.0
                                WHEN r.Reference LIKE $cleanQuery || '%' OR r.Reference LIKE '% ' || $cleanQuery || '%' THEN -2000.0
                                WHEN r.Reference LIKE '%' || $cleanQuery || '%' THEN -1000.0
                                WHEN (r.Transliteration LIKE $leadWord || ' %' OR r.Transliteration LIKE $leadWord || CHAR(10) || '%')
                                 AND (r.Synonyms LIKE $leadWord || '—%' OR r.Synonyms LIKE $leadWord || '-%') THEN -600.0
                                WHEN (r.Transliteration LIKE $leadWord || ' %' OR r.Transliteration LIKE $leadWord || CHAR(10) || '%') THEN -400.0
                                WHEN (r.Synonyms LIKE $leadWord || '—%' OR r.Synonyms LIKE $leadWord || '-%') THEN -200.0
                                ELSE 0.0
                             END 
                             + CASE WHEN length(r.Purports) > 100 THEN -20.0 ELSE 0.0 END
                             + {rankExpression})";
                    }

                    querySql += $" ORDER BY {orderExpression} LIMIT $limit OFFSET $offset";

                    queryCmd.CommandText = querySql;
                    queryCmd.Parameters.AddWithValue("$query", ftsQuery);
                    queryCmd.Parameters.AddWithValue("$cleanQuery", query.Trim());
                    queryCmd.Parameters.AddWithValue("$leadWord", leadWord);
                    queryCmd.Parameters.AddWithValue("$limit", limit);
                    queryCmd.Parameters.AddWithValue("$offset", offset);

                    string cleanQuery = System.Text.RegularExpressions.Regex.Replace(query.TrimStart('@').Trim(), @"[^\w]", "").ToUpperInvariant();

                    using (var reader = queryCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string rk = reader.GetString(0);
                            string bk = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            string refText = reader.IsDBNull(2) ? "" : reader.GetString(2);
                            int seq = reader.GetInt32(3);
                            int exactPriority = reader.IsDBNull(4) ? 4 : reader.GetInt32(4);

                            string title = BookTitles.TryGetValue(bk, out var t) ? t : bk;

                            bool isExact = exactPriority <= 2;
                            if (!isExact && cleanQuery.Length >= 2)
                            {
                                string cr = System.Text.RegularExpressions.Regex.Replace(refText, @"[^\w]", "").ToUpperInvariant();
                                string ck = System.Text.RegularExpressions.Regex.Replace(rk, @"[^\w]", "").ToUpperInvariant();
                                if (cr == cleanQuery || ck == cleanQuery ||
                                    cr.EndsWith(cleanQuery) || ck.EndsWith(cleanQuery) ||
                                    string.Equals(refText, query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(rk, query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                    (bookKey != null && (bookKey.ToUpperInvariant() + cleanQuery == cr || bookKey.ToUpperInvariant() + cleanQuery == ck)) ||
                                    (!string.IsNullOrEmpty(bk) && (bk.ToUpperInvariant() + cleanQuery == cr || bk.ToUpperInvariant() + cleanQuery == ck)))
                                {
                                    isExact = true;
                                }
                            }

                            results.Add(new SearchResult
                            {
                                RecordKey = rk,
                                BookKey = bk ?? "",
                                Reference = string.IsNullOrWhiteSpace(refText) ? (bk ?? "") : refText,
                                BookTitle = title,
                                Preview = "",
                                Category = "Scripture",
                                Sequence = seq,
                                IsExactMatch = isExact
                            });
                        }
                    }


                    // 4. Fetch highlighted snippets only for the winning display results
                    if (results.Count > 0)
                    {
                        using var snipCmd = connection.CreateCommand();
                        var placeholders = new List<string>();
                        for (int i = 0; i < results.Count; i++)
                        {
                            string p = $"$sk{i}";
                            placeholders.Add(p);
                            snipCmd.Parameters.AddWithValue(p, results[i].RecordKey);
                        }
                        snipCmd.CommandText = $@"
                            SELECT RecordKey, snippet(RecordsFts, -1, '«', '»', '...', 25)
                            FROM RecordsFts
                            WHERE RecordKey IN ({string.Join(",", placeholders)}) AND RecordsFts MATCH $q";
                        snipCmd.Parameters.AddWithValue("$q", ftsQuery);

                        var snipMap = new Dictionary<string, string>();
                        using (var snipReader = snipCmd.ExecuteReader())
                        {
                            while (snipReader.Read())
                            {
                                string k = snipReader.GetString(0);
                                string s = snipReader.IsDBNull(1) ? "" : snipReader.GetString(1);
                                snipMap[k] = s;
                            }
                        }

                        foreach (var r in results)
                        {
                            if (snipMap.TryGetValue(r.RecordKey, out var snip) && !string.IsNullOrWhiteSpace(snip))
                            {
                                r.Preview = snip;
                            }
                        }
                    }
                }
                catch (SqliteException)
                {
                    // Syntax error in FTS5 query or schema mismatch: safe fallback to 0 results
                }
            });

            return (results, totalCount);
        }

        private int CanonicalBookIndex(string bookKey)
        {
            var order = _bookRegistry != null ? (List<string>)_bookRegistry.GetCanonicalBookOrder() : CanonicalBookOrder;
            int idx = order.IndexOf(bookKey);
            return idx == -1 ? order.Count : idx; // unknown keys sort after all known books, before nothing
        }
    }
}
