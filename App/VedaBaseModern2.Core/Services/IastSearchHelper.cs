using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Provides utilities for bridging IAST (International Alphabet of Sanskrit Transliteration)
    /// diacritics with plain English queries across SQLite GLOB and Regex matching, ensuring
    /// queries like "bhunjana" and "bhuñjāna" match identically regardless of Match Case / Exact Word settings.
    /// </summary>
    public static class IastSearchHelper
    {
        private static readonly Dictionary<char, string> IastCharToGlobClass = new()
        {
            ['a'] = "[aā]", ['A'] = "[AĀ]", ['ā'] = "[aā]", ['Ā'] = "[AĀ]",
            ['i'] = "[iī]", ['I'] = "[IĪ]", ['ī'] = "[iī]", ['Ī'] = "[IĪ]",
            ['u'] = "[uū]", ['U'] = "[UŪ]", ['ū'] = "[uū]", ['Ū'] = "[UŪ]",
            ['r'] = "[rṛṝ]", ['R'] = "[RṚṜ]", ['ṛ'] = "[rṛṝ]", ['Ṛ'] = "[RṚṜ]", ['ṝ'] = "[rṛṝ]", ['Ṝ'] = "[RṚṜ]",
            ['l'] = "[lḷḹ]", ['L'] = "[LḶḸ]", ['ḷ'] = "[lḷḹ]", ['Ḷ'] = "[LḶḸ]", ['ḹ'] = "[lḷḹ]", ['Ḹ'] = "[LḶḸ]",
            ['e'] = "[eē]", ['E'] = "[EĒ]", ['ē'] = "[eē]", ['Ē'] = "[EĒ]",
            ['o'] = "[oō]", ['O'] = "[OŌ]", ['ō'] = "[oō]", ['Ō'] = "[OŌ]",
            ['m'] = "[mṁṃ]", ['M'] = "[MṀṂ]", ['ṁ'] = "[mṁṃ]", ['Ṁ'] = "[MṀṂ]", ['ṃ'] = "[mṁṃ]", ['Ṃ'] = "[MṀṂ]",
            ['h'] = "[hḥ]", ['H'] = "[HḤ]", ['ḥ'] = "[hḥ]", ['Ḥ'] = "[HḤ]",
            ['n'] = "[nñṅṇ]", ['N'] = "[NÑṄṆ]", ['ñ'] = "[nñṅṇ]", ['Ñ'] = "[NÑṄṆ]", ['ṅ'] = "[nñṅṇ]", ['Ṅ'] = "[NÑṄṆ]", ['ṇ'] = "[nñṅṇ]", ['Ṇ'] = "[NÑṄṆ]",
            ['t'] = "[tṭ]", ['T'] = "[TṬ]", ['ṭ'] = "[tṭ]", ['Ṭ'] = "[TṬ]",
            ['d'] = "[dḍ]", ['D'] = "[DḌ]", ['ḍ'] = "[dḍ]", ['Ḍ'] = "[DḌ]",
            ['s'] = "[sśṣ]", ['S'] = "[SŚṢ]", ['ś'] = "[sśṣ]", ['Ś'] = "[SŚṢ]", ['ṣ'] = "[sśṣ]", ['Ṣ'] = "[SŚṢ]"
        };

        private static readonly Dictionary<string, string[]> CommonPhoneticVariants = new(StringComparer.OrdinalIgnoreCase)
        {
            ["krishna"] = new[] { "krishna", "krsna" },
            ["krsna"] = new[] { "krsna", "krishna" },
            ["kṛṣṇa"] = new[] { "kṛṣṇa", "krsna", "krishna" },
            ["chaitanya"] = new[] { "chaitanya", "caitanya" },
            ["caitanya"] = new[] { "caitanya", "chaitanya" },
            ["shiva"] = new[] { "shiva", "siva" },
            ["siva"] = new[] { "siva", "shiva" },
            ["vishnu"] = new[] { "vishnu", "visnu" },
            ["visnu"] = new[] { "visnu", "vishnu" },
            ["vrindavan"] = new[] { "vrindavan", "vrindavana", "vrndavana" },
            ["sankirtan"] = new[] { "sankirtan", "sankirtana" }
        };

        /// <summary>
        /// Converts an input word or phrase into a case-preserving SQLite GLOB pattern
        /// that transparently matches both standard English and IAST diacritical characters.
        /// E.g. "bhunjana" -> "*b[hḥ][uū][nñṅṇ]j[aā][nñṅṇ][aā]*"
        /// </summary>
        public static string ToIastGlobPattern(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "*";

            var sb = new StringBuilder("*");
            foreach (char ch in text)
            {
                if (IastCharToGlobClass.TryGetValue(ch, out var globClass))
                {
                    sb.Append(globClass);
                }
                else if (ch == '*' || ch == '?' || ch == '[' || ch == ']')
                {
                    sb.Append('[').Append(ch).Append(']');
                }
                else
                {
                    sb.Append(ch);
                }
            }
            sb.Append('*');
            return sb.ToString();
        }

        /// <summary>
        /// Converts an input word into an IAST-tolerant regular expression pattern for C# Regex.
        /// E.g. "bhunjana" -> "b[hḥ][uū][nñṅṇ]j[aā][nñṅṇ][aā]"
        /// </summary>
        public static string ToIastRegexPattern(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (IastCharToGlobClass.TryGetValue(ch, out var globClass))
                {
                    sb.Append(globClass);
                }
                else if (@".*+?^${}()|[]\".Contains(ch))
                {
                    sb.Append('\\').Append(ch);
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds case-matching SQL predicates with IAST-neutral GLOB patterns,
        /// ensuring that queries like "bhunjana" correctly match "bhuñjāna" while
        /// still honoring the user's case intent (e.g. lowercase vs uppercase).
        /// </summary>
        public static string BuildCaseGlobSqlClause(string query, SqliteCommand cmd, string paramPrefix)
        {
            if (string.IsNullOrWhiteSpace(query)) return string.Empty;

            var terms = ExtractTerms(query);
            if (terms.Count == 0) return string.Empty;

            var andClauses = new List<string>();
            int termIndex = 0;

            foreach (var term in terms)
            {
                var variants = GetVariants(term);
                var globParamNames = new List<string>();

                for (int v = 0; v < variants.Count; v++)
                {
                    string pName = $"${paramPrefix}_{termIndex}_{v}";
                    cmd.Parameters.AddWithValue(pName, ToIastGlobPattern(variants[v]));
                    globParamNames.Add(pName);
                }

                // Any variant can match in any of the primary text columns
                var columnChecks = new List<string>();
                foreach (var p in globParamNames)
                {
                    columnChecks.Add($"r.Transliteration GLOB {p}");
                    columnChecks.Add($"r.Synonyms GLOB {p}");
                    columnChecks.Add($"r.Translation GLOB {p}");
                    columnChecks.Add($"r.Purports GLOB {p}");
                    columnChecks.Add($"r.Reference GLOB {p}");
                }

                andClauses.Add("(" + string.Join(" OR ", columnChecks) + ")");
                termIndex++;
            }

            return andClauses.Count > 0 ? " AND " + string.Join(" AND ", andClauses) : string.Empty;
        }

        private static List<string> ExtractTerms(string query)
        {
            var terms = new List<string>();
            string trimmed = query.Trim();

            // If the query is an exact phrase wrapped in quotes, treat as a single phrase
            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
            {
                string inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (!string.IsNullOrEmpty(inner))
                {
                    terms.Add(inner);
                    return terms;
                }
            }

            // Otherwise extract words, omitting boolean keywords
            var matches = Regex.Matches(trimmed, @"[\p{L}\p{N}\-]+");
            foreach (Match m in matches)
            {
                string word = m.Value.Trim();
                if (string.IsNullOrEmpty(word)) continue;
                if (word.Equals("AND", StringComparison.Ordinal) ||
                    word.Equals("OR", StringComparison.Ordinal) ||
                    word.Equals("NOT", StringComparison.Ordinal) ||
                    word.Equals("NEAR", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                terms.Add(word);
            }

            if (terms.Count == 0 && !string.IsNullOrWhiteSpace(trimmed))
            {
                terms.Add(trimmed);
            }

            return terms;
        }

        private static List<string> GetVariants(string term)
        {
            var variants = new List<string> { term };

            // Check if there are common phonetic equivalents (e.g. krishna <-> krsna)
            string clean = Regex.Replace(term, @"[^\p{L}]", "");
            if (!string.IsNullOrEmpty(clean) && CommonPhoneticVariants.TryGetValue(clean, out var phonetics))
            {
                bool isAllUpper = term.All(c => !char.IsLetter(c) || char.IsUpper(c));
                bool isTitle = term.Length > 0 && char.IsUpper(term[0]) && term.Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c));

                foreach (var ph in phonetics)
                {
                    string formatted = ph;
                    if (isAllUpper) formatted = ph.ToUpperInvariant();
                    else if (isTitle) formatted = char.ToUpperInvariant(ph[0]) + ph.Substring(1);

                    if (!variants.Contains(formatted, StringComparer.Ordinal))
                    {
                        variants.Add(formatted);
                    }
                }
            }

            return variants;
        }
    }
}
