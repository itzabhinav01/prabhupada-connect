using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// One work's grammar definition for "@" direct-reference navigation.
    /// Adding a new work means adding one entry here - no parser rewrite.
    /// </summary>
    public class ReferenceWorkDefinition
    {
        public required string BookKey { get; init; }
        public required string DisplayTitle { get; init; }

        /// <summary>Alias tokens a user might type, normalized (uppercase, no
        /// spaces/punctuation) at registration time. The BookKey itself is
        /// always implicitly included.</summary>
        public required string[] Aliases { get; init; }

        /// <summary>How many dot-separated numeric parts this work's verse
        /// references have. 2 for "Bg 1.1"-style (chapter.verse) works, 3 for
        /// "SB 1.4.6"-style (canto.chapter.verse) works.</summary>
        public required int NumericLevels { get; init; }

        /// <summary>Human labels for each numeric level, outermost first
        /// (e.g. ["Canto","Chapter","Verse"] for SB, ["Chapter","Verse"] for BG).</summary>
        public required string[] LevelNames { get; init; }
    }

    /// <summary>A single navigable node in the reference index - either an
    /// intermediate level (a chapter, a canto) or a leaf verse.</summary>
    public class ReferenceSuggestion
    {
        public required string DisplayText { get; init; } // "BG 1" or "BG 1.1 — Dhrtarastra asks..."
        public required string SubText { get; init; } = "";
        public string? RecordKey { get; init; } // set only for an exact, navigable leaf verse
        public required string QueryToComplete { get; init; } // what typing/clicking this fills the search box with, e.g. "@BG 1" or "@BG 1.1"
        public bool IsWork { get; init; }
    }

    /// <summary>
    /// Parses "@..." direct-reference queries (grammar) and resolves them
    /// against an in-memory index built once from the corpus hierarchy
    /// (performance - never re-queries SQLite per keystroke). See
    /// PHASE_CONTINUATION_REPORT.md "Direct Reference Grammar" for the full
    /// documented grammar.
    /// </summary>
    public class DirectReferenceService
    {
        private readonly ICorpusRepository _repository;

        // Extensible work registry - add an entry here to support a new work.
        // BS/TLK/MM/NBS/SSR/EJ are included with only their BookKey as
        // an alias (no colloquial alias known/confirmed yet) so they are still
        // reachable via "@BS 5.1" etc. without inventing unverified aliases.
        private static readonly ReferenceWorkDefinition[] Works = new[]
        {
            new ReferenceWorkDefinition { BookKey = "BG", DisplayTitle = "Bhagavad-gītā As It Is", NumericLevels = 2, LevelNames = new[] { "Chapter", "Verse" }, Aliases = new[] { "BG", "BHAGAVADGITA", "GITA", "BHAGAVADGITA" } },
            new ReferenceWorkDefinition { BookKey = "SB", DisplayTitle = "Śrīmad-Bhāgavatam", NumericLevels = 3, LevelNames = new[] { "Canto", "Chapter", "Verse" }, Aliases = new[] { "SB", "BHAGAVATAM", "SRIMADBHAGAVATAM" } },
            new ReferenceWorkDefinition { BookKey = "DI", DisplayTitle = "Śrī Caitanya-caritāmṛta — Ādi-līlā", NumericLevels = 2, LevelNames = new[] { "Chapter", "Verse" }, Aliases = new[] { "ADI", "CCADI", "ADILILA" } },
            new ReferenceWorkDefinition { BookKey = "MADHYA", DisplayTitle = "Śrī Caitanya-caritāmṛta — Madhya-līlā", NumericLevels = 2, LevelNames = new[] { "Chapter", "Verse" }, Aliases = new[] { "MADHYA", "CCMADHYA", "MADHYALILA" } },
            new ReferenceWorkDefinition { BookKey = "ANTYA", DisplayTitle = "Śrī Caitanya-caritāmṛta — Antya-līlā", NumericLevels = 2, LevelNames = new[] { "Chapter", "Verse" }, Aliases = new[] { "ANTYA", "CCANTYA", "ANTYALILA" } },
            new ReferenceWorkDefinition { BookKey = "ISO", DisplayTitle = "Śrī Īśopaniṣad", NumericLevels = 1, LevelNames = new[] { "Mantra" }, Aliases = new[] { "ISO", "ISOPANISAD" } },
            new ReferenceWorkDefinition { BookKey = "NOI", DisplayTitle = "The Nectar of Instruction", NumericLevels = 1, LevelNames = new[] { "Verse" }, Aliases = new[] { "NOI", "NECTAROFINSTRUCTION" } },
            new ReferenceWorkDefinition { BookKey = "BS", DisplayTitle = "Śrī Brahma-saṁhitā", NumericLevels = 2, LevelNames = new[] { "Chapter", "Verse" }, Aliases = new[] { "BS", "BRAHMASAMHITA" } },
            new ReferenceWorkDefinition { BookKey = "TLK", DisplayTitle = "Teachings of Lord Kapila", NumericLevels = 1, LevelNames = new[] { "Verse" }, Aliases = new[] { "TLK" } },
            new ReferenceWorkDefinition { BookKey = "MM", DisplayTitle = "Mukunda-mālā-stotra", NumericLevels = 1, LevelNames = new[] { "Verse" }, Aliases = new[] { "MM" } },
            new ReferenceWorkDefinition { BookKey = "NBS", DisplayTitle = "Nārada-bhakti-sūtra", NumericLevels = 1, LevelNames = new[] { "Sūtra" }, Aliases = new[] { "NBS" } },
            new ReferenceWorkDefinition { BookKey = "SSR", DisplayTitle = "The Science of Self-Realization", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "SSR" } },
            new ReferenceWorkDefinition { BookKey = "EJ", DisplayTitle = "Easy Journey to Other Planets", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "EJ" } },
            new ReferenceWorkDefinition { BookKey = "SPS", DisplayTitle = "Śrīla Prabhupāda Ślokas", NumericLevels = 2, LevelNames = new[] { "Section", "Verse" }, Aliases = new[] { "SPS", "SLOKAS", "SHLOKAS", "PRABHUPADASLOKAS" } },

            // The 30 books added to the corpus in Phase 9 (prose/anthology
            // works ingested via ProseBookSourceExtractor). Every one of
            // these stages its Reference field as a single leading chapter
            // number ("TLC 1: Teachings to Rūpa Gosvāmī", "LoB verse 1",
            // "GG: Chapter 1", etc.), so NumericLevels = 1 / OneLevelPattern
            // is correct for all of them regardless of the exact wording -
            // confirmed against the actual staged data, not the manifest's
            // more generic {Chapter}-{Section} recordKeyStrategy label.
            // BookKey values match the real corpus/manifest keys exactly
            // (e.g. "KB" for Kṛṣṇa Book, "LON" for The Laws of Nature) -
            // colloquial names are registered as aliases instead.
            new ReferenceWorkDefinition { BookKey = "NOD", DisplayTitle = "The Nectar of Devotion", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "NOD", "NECTAROFDEVOTION" } },
            new ReferenceWorkDefinition { BookKey = "TLC", DisplayTitle = "Teachings of Lord Caitanya", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "TLC", "TEACHINGSOFLORDCAITANYA" } },
            new ReferenceWorkDefinition { BookKey = "KB", DisplayTitle = "Kṛṣṇa, the Supreme Personality of Godhead", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "KB", "KRSNA", "KRISHNA", "KRSNABOOK" } },
            new ReferenceWorkDefinition { BookKey = "TQK", DisplayTitle = "Teachings of Queen Kuntī", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "TQK", "QUEENKUNTI", "TEACHINGSOFQUEENKUNTI" } },
            new ReferenceWorkDefinition { BookKey = "BB", DisplayTitle = "Bṛhad-bhāgavatāmṛta", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "BB", "BRHADBHAGAVATAMRTA" } },
            new ReferenceWorkDefinition { BookKey = "DS", DisplayTitle = "Dialectical Spiritualism", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "DS", "DIALECTICALSPIRITUALISM" } },
            new ReferenceWorkDefinition { BookKey = "BBD", DisplayTitle = "Beyond Birth and Death", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "BBD", "BEYONDBIRTHANDDEATH" } },
            new ReferenceWorkDefinition { BookKey = "POY", DisplayTitle = "The Perfection of Yoga", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "POY", "PERFECTIONOFYOGA" } },
            new ReferenceWorkDefinition { BookKey = "RV", DisplayTitle = "Rāja-Vidyā: The King of Knowledge", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "RV", "RAJAVIDYA" } },
            new ReferenceWorkDefinition { BookKey = "EKC", DisplayTitle = "Elevation to Kṛṣṇa Consciousness", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "EKC", "ELEVATIONTOKRSNACONSCIOUSNESS" } },
            new ReferenceWorkDefinition { BookKey = "KCTYS", DisplayTitle = "Kṛṣṇa Consciousness: The Topmost Yoga System", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "KCTYS", "TOPMOSTYOGASYSTEM" } },
            new ReferenceWorkDefinition { BookKey = "MOG", DisplayTitle = "Message of Godhead", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "MOG", "MESSAGEOFGODHEAD" } },
            new ReferenceWorkDefinition { BookKey = "LOB", DisplayTitle = "Light of the Bhāgavata", NumericLevels = 1, LevelNames = new[] { "Verse" }, Aliases = new[] { "LOB", "LIGHTOFTHEBHAGAVATA" } },
            new ReferenceWorkDefinition { BookKey = "PQPA", DisplayTitle = "Perfect Questions, Perfect Answers", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "PQPA", "PERFECTQUESTIONSPERFECTANSWERS" } },
            new ReferenceWorkDefinition { BookKey = "JSD", DisplayTitle = "The Journey of Self-Discovery", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "JSD", "JOURNEYOFSELFDISCOVERY" } },
            new ReferenceWorkDefinition { BookKey = "LCFL", DisplayTitle = "Life Comes From Life", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "LCFL", "LIFECOMESFROMLIFE" } },
            new ReferenceWorkDefinition { BookKey = "CB", DisplayTitle = "Coming Back: The Science of Reincarnation", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "CB", "COMINGBACK" } },
            new ReferenceWorkDefinition { BookKey = "CAT", DisplayTitle = "Civilization and Transcendence", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "CAT", "CIVILIZATIONANDTRANSCENDENCE" } },
            new ReferenceWorkDefinition { BookKey = "OWK", DisplayTitle = "On the Way to Kṛṣṇa", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "OWK", "ONTHEWAYTOKRSNA" } },
            new ReferenceWorkDefinition { BookKey = "SFL", DisplayTitle = "The Search for Liberation", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "SFL", "SEARCHFORLIBERATION" } },
            new ReferenceWorkDefinition { BookKey = "TT", DisplayTitle = "Transcendental Teachings of Prahlāda Mahārāja", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "TT" } },
            new ReferenceWorkDefinition { BookKey = "SC", DisplayTitle = "A Second Chance", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "SC", "SECONDCHANCE" } },
            new ReferenceWorkDefinition { BookKey = "DWT", DisplayTitle = "Dharma: The Way of Transcendence", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "DWT", "DHARMA" } },
            new ReferenceWorkDefinition { BookKey = "POP", DisplayTitle = "Path of Perfection", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "POP", "PATHOFPERFECTION" } },
            new ReferenceWorkDefinition { BookKey = "QFE", DisplayTitle = "Quest for Enlightenment", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "QFE", "QUESTFORENLIGHTENMENT" } },
            new ReferenceWorkDefinition { BookKey = "RTW", DisplayTitle = "Renunciation Through Wisdom", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "RTW", "RENUNCIATIONTHROUGHWISDOM" } },
            new ReferenceWorkDefinition { BookKey = "LON", DisplayTitle = "The Laws of Nature: An Infallible Justice", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "LON", "LAWSOFNATURE" } },
            new ReferenceWorkDefinition { BookKey = "MG", DisplayTitle = "Matchless Gift", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "MG", "MATCHLESSGIFT" } },
            new ReferenceWorkDefinition { BookKey = "ROP", DisplayTitle = "Reservoir of Pleasure", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "ROP", "RESERVOIROFPLEASURE" } },
            new ReferenceWorkDefinition { BookKey = "GG", DisplayTitle = "Gītār Gāna", NumericLevels = 1, LevelNames = new[] { "Chapter" }, Aliases = new[] { "GG", "GITARGANA" } },
        };

        private static readonly Dictionary<string, ReferenceWorkDefinition> AliasLookup = BuildAliasLookup();

        private static Dictionary<string, ReferenceWorkDefinition> BuildAliasLookup()
        {
            var map = new Dictionary<string, ReferenceWorkDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var work in Works)
            {
                map[work.BookKey] = work;
                foreach (var alias in work.Aliases) map[alias] = work;
            }
            return map;
        }

        // The regexes below extract the LAST N dot-separated integers from a
        // stored Reference string, regardless of the leading book word's exact
        // spelling/diacritics (e.g. "Ādi" vs "Adi") - this is deliberately
        // tolerant so index-building never depends on exact prefix formatting.
        private static readonly Regex ThreeLevelPattern = new(@"(\d+)\.(\d+)\.(\d+)");
        private static readonly Regex TwoLevelPattern = new(@"(\d+)\.(\d+)");
        private static readonly Regex OneLevelPattern = new(@"(\d+)");

        // Index: BookKey -> ordered list of (numeric parts, RecordKey, Reference)
        private Dictionary<string, List<(int[] Parts, string RecordKey, string Reference)>>? _index;
        private readonly object _indexLock = new();

        public DirectReferenceService(ICorpusRepository repository)
        {
            _repository = repository;
        }

        private async Task EnsureIndexAsync()
        {
            if (_index != null) return;
            var hierarchy = await _repository.GetLibraryHierarchyAsync();
            var byKey = hierarchy.ToDictionary(b => b.BookKey, b => b);

            var index = new Dictionary<string, List<(int[], string, string)>>();
            foreach (var work in Works)
            {
                if (!byKey.TryGetValue(work.BookKey, out var bookNode)) continue;

                var pattern = work.NumericLevels switch
                {
                    3 => ThreeLevelPattern,
                    2 => TwoLevelPattern,
                    _ => OneLevelPattern
                };

                var entries = new List<(int[], string, string)>();
                foreach (var chapter in bookNode.Chapters)
                {
                    foreach (var rec in chapter.Records)
                    {
                        var m = pattern.Match(rec.Reference);
                        if (!m.Success) continue; // non-numeric records (e.g. "Iso Invocation") are not @-navigable by number
                        var parts = new int[work.NumericLevels];
                        bool ok = true;
                        for (int i = 0; i < work.NumericLevels; i++)
                        {
                            if (!int.TryParse(m.Groups[i + 1].Value, out parts[i])) { ok = false; break; }
                        }
                        if (ok) entries.Add((parts, rec.RecordKey, rec.Reference));
                    }
                }
                // Stable order by numeric parts (List<T>.Sort is NOT
                // guaranteed stable, so ties are not left to chance - there
                // should never be a genuine tie post-hierarchy-filtering, since
                // GetLibraryHierarchyAsync already excludes duplicate-content
                // '#'-suffixed keys, but this keeps resolution deterministic
                // defensively rather than relying on that invariant alone).
                index[work.BookKey] = entries
                    .Select((e, originalIndex) => (e, originalIndex))
                    .OrderBy(x => x.e.Item1, new IntArrayComparer())
                    .ThenBy(x => x.originalIndex)
                    .Select(x => x.e)
                    .ToList();
            }

            lock (_indexLock)
            {
                _index ??= index;
            }
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(capacity: normalized.Length);
            foreach (var c in normalized)
            {
                var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        /// <summary>True if the raw search-box text should be treated as
        /// direct-reference mode (starts with '@').</summary>
        public static bool IsReferenceQuery(string rawText) => rawText.TrimStart().StartsWith("@");

        /// <summary>
        /// Parses the portion after '@' into (work, numeric parts typed so
        /// far). Tolerant of case, optional spaces, and missing separators
        /// ("@bg 1.1", "@BG1.1", "@Bg 1.1" all parse identically). Returns
        /// null only if nothing after '@' can even be interpreted as the
        /// start of a work name.
        /// </summary>
        public static (ReferenceWorkDefinition? Work, string WorkTextTyped, int[] Parts) Parse(string rawText)
        {
            string text = rawText.TrimStart();
            if (text.StartsWith("@")) text = text.Substring(1);
            text = text.Trim();

            if (text.Length == 0) return (null, "", Array.Empty<int>());

            string cleanText = RemoveDiacritics(text);
            var m = Regex.Match(cleanText, @"^([A-Za-z\-]+(?:\s+[A-Za-z\-]+)*)\s*([\d][\d.\s]*)?$");
            if (!m.Success) return (null, text, Array.Empty<int>());

            string workText = m.Groups[1].Value;
            string numericText = m.Groups[2].Success ? m.Groups[2].Value : "";

            string normalized = Regex.Replace(workText, @"[^A-Za-z]", "").ToUpperInvariant();
            AliasLookup.TryGetValue(normalized, out var work);

            // If the full concatenation didn't match (e.g. "CC Adi" typed but
            // only "ADI" is registered), retry with just the LAST word - lets
            // "@CC Adi 1.1" and "@Adi 1.1" resolve identically.
            if (work == null)
            {
                var lastWord = workText.Trim().Split(' ').LastOrDefault();
                if (!string.IsNullOrEmpty(lastWord))
                {
                    string lastNorm = Regex.Replace(lastWord, @"[^A-Za-z]", "").ToUpperInvariant();
                    AliasLookup.TryGetValue(lastNorm, out work);
                }
            }

            var parts = numericText
                .Split(new[] { '.', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => int.TryParse(p, out var v) ? v : (int?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToArray();

            return (work, workText, parts);
        }

        /// <summary>
        /// Resolves a fully-specified reference (one numeric part per
        /// NumericLevels the work declares) to an exact RecordKey, or null if
        /// no such verse exists. Used for Enter-to-navigate and exact-click.
        /// </summary>
        public async Task<string?> TryResolveExactAsync(string rawText)
        {
            await EnsureIndexAsync();
            var (work, _, parts) = Parse(rawText);
            if (work == null || parts.Length != work.NumericLevels) return null;
            if (_index == null || !_index.TryGetValue(work.BookKey, out var entries)) return null;

            foreach (var (entryParts, recordKey, _) in entries)
            {
                if (entryParts.SequenceEqual(parts)) return recordKey;
            }
            return null;
        }

        /// <summary>
        /// Progressive autocomplete suggestions for the current "@..." text,
        /// bounded and fast (pure in-memory index lookup, no SQLite access
        /// per keystroke). See grammar doc for exact behavior per input stage.
        /// </summary>
        public async Task<List<ReferenceSuggestion>> GetSuggestionsAsync(string rawText, int maxResults = 20)
        {
            await EnsureIndexAsync();
            var (work, workText, parts) = Parse(rawText);

            // Stage 0: bare "@" or unrecognized work prefix so far -> list works.
            if (work == null)
            {
                string filter = Regex.Replace(workText, @"[^A-Za-z]", "").ToUpperInvariant();

                // Special handling for "@CC" query: suggest the three Caitanya-caritāmṛta divisions
                if (filter == "CC" || filter.StartsWith("CAITANYA"))
                {
                    return new List<ReferenceSuggestion>
                    {
                        new ReferenceSuggestion
                        {
                            DisplayText = "Śrī Caitanya-caritāmṛta — Ādi-līlā",
                            SubText = "ADI",
                            QueryToComplete = "@CC Adi ",
                            IsWork = true
                        },
                        new ReferenceSuggestion
                        {
                            DisplayText = "Śrī Caitanya-caritāmṛta — Madhya-līlā",
                            SubText = "MADHYA",
                            QueryToComplete = "@CC Madhya ",
                            IsWork = true
                        },
                        new ReferenceSuggestion
                        {
                            DisplayText = "Śrī Caitanya-caritāmṛta — Antya-līlā",
                            SubText = "ANTYA",
                            QueryToComplete = "@CC Antya ",
                            IsWork = true
                        }
                    };
                }

                return Works
                    .Where(w => string.IsNullOrEmpty(filter) || w.BookKey.StartsWith(filter, StringComparison.OrdinalIgnoreCase) || w.DisplayTitle.ToUpperInvariant().Contains(filter) || w.Aliases.Any(a => a.StartsWith(filter, StringComparison.OrdinalIgnoreCase)))
                    .Take(maxResults)
                    .Select(w => new ReferenceSuggestion
                    {
                        DisplayText = w.DisplayTitle,
                        SubText = w.BookKey,
                        QueryToComplete = $"@{w.BookKey} ",
                        IsWork = true
                    })
                    .ToList();
            }

            if (_index == null || !_index.TryGetValue(work.BookKey, out var entries) || entries.Count == 0)
                return new List<ReferenceSuggestion>();

            // Stage: fully-specified reference -> the exact verse (single result).
            if (parts.Length >= work.NumericLevels)
            {
                var exact = entries.FirstOrDefault(e => e.Parts.Take(work.NumericLevels).SequenceEqual(parts.Take(work.NumericLevels)));
                if (exact.RecordKey != null)
                {
                    return new List<ReferenceSuggestion>
                    {
                        new ReferenceSuggestion
                        {
                            DisplayText = exact.Reference,
                            SubText = work.DisplayTitle,
                            RecordKey = exact.RecordKey,
                            QueryToComplete = $"@{work.BookKey} {string.Join(".", exact.Parts)}"
                        }
                    };
                }
                return new List<ReferenceSuggestion>();
            }

            // Stage: partial numeric prefix -> list matching next-level entries,
            // deduplicated on the parts prefix one level deeper than typed.
            var matches = entries.Where(e => parts.Length == 0 || e.Parts.Take(parts.Length).SequenceEqual(parts)).ToList();
            int nextLevelDepth = parts.Length + 1;

            if (nextLevelDepth >= work.NumericLevels)
            {
                // Next level IS the verse level - list actual verses.
                return matches
                    .Take(maxResults)
                    .Select(e => new ReferenceSuggestion
                    {
                        DisplayText = e.Reference,
                        SubText = work.DisplayTitle,
                        RecordKey = e.RecordKey,
                        QueryToComplete = $"@{work.BookKey} {string.Join(".", e.Parts)}"
                    })
                    .ToList();
            }
            else
            {
                // Next level is an intermediate grouping (e.g. SB Canto -> Chapter).
                var seen = new HashSet<string>();
                var results = new List<ReferenceSuggestion>();
                foreach (var e in matches)
                {
                    var prefixParts = e.Parts.Take(nextLevelDepth).ToArray();
                    string key = string.Join(".", prefixParts);
                    if (!seen.Add(key)) continue;
                    results.Add(new ReferenceSuggestion
                    {
                        DisplayText = $"{work.BookKey} {key} — {work.LevelNames[nextLevelDepth - 1]} {prefixParts[^1]}",
                        SubText = work.DisplayTitle,
                        QueryToComplete = $"@{work.BookKey} {key}.",
                        IsWork = false
                    });
                    if (results.Count >= maxResults) break;
                }
                return results;
            }
        }

        private sealed class IntArrayComparer : IComparer<int[]>
        {
            public int Compare(int[]? a, int[]? b)
            {
                if (a == null || b == null) return 0;
                for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                {
                    int c = a[i].CompareTo(b[i]);
                    if (c != 0) return c;
                }
                return a.Length.CompareTo(b.Length);
            }
        }
    }
}
