using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Preprocesses user search queries into safe, optimized SQLite FTS5 queries.
    /// Handles quote balancing, disarms bare boolean keywords, sanitizes punctuation,
    /// expands common Anglicized/IAST phonetic pairs, and normalizes Devanagari script.
    /// </summary>
    public static class FtsQueryParser
    {
        private static readonly Dictionary<string, string[]> PhoneticExpansions = new(StringComparer.OrdinalIgnoreCase)
        {
            { "krishna", new[] { "krishna", "krsna", "kṛṣṇa" } },
            { "krsna", new[] { "krsna", "kṛṣṇa", "krishna" } },
            { "kṛṣṇa", new[] { "kṛṣṇa", "krsna", "krishna" } },
            { "chaitanya", new[] { "chaitanya", "caitanya" } },
            { "caitanya", new[] { "caitanya", "chaitanya" } },
            { "shiva", new[] { "shiva", "siva", "śiva" } },
            { "siva", new[] { "siva", "śiva", "shiva" } },
            { "vishnu", new[] { "vishnu", "visnu", "viṣṇu" } },
            { "visnu", new[] { "visnu", "viṣṇu", "vishnu" } },
            { "vrindavan", new[] { "vrindavan", "vrindavana", "vrndavana", "vṛndāvana" } },
            { "vrindavana", new[] { "vrindavana", "vrndavana", "vṛndāvana", "vrindavan" } },
            { "kurukshetra", new[] { "kurukshetra", "kuruksetra", "kuru-kṣetra" } },
            { "kuruksetra", new[] { "kuruksetra", "kuru-kṣetra", "kurukshetra" } },
            { "prabhupada", new[] { "prabhupada", "prabhupāda", "śrīla prabhupāda" } },
            { "prabhupāda", new[] { "prabhupāda", "prabhupada" } },
            { "arjuna", new[] { "arjuna", "arjun" } },
            { "yashoda", new[] { "yashoda", "yasoda", "yaśodā" } },
            { "radha", new[] { "radha", "rādhā", "radharani", "rādhārāṇī" } },
            { "rādhā", new[] { "rādhā", "radha", "radharani" } },
            { "radharani", new[] { "radharani", "radha", "rādhārāṇī" } },
            { "sankirtan", new[] { "sankirtan", "sankirtana", "saṅkīrtana" } },
            { "sankirtana", new[] { "sankirtana", "saṅkīrtana", "sankirtan" } },
            { "saṅkīrtana", new[] { "saṅkīrtana", "sankirtana", "sankirtan" } },
            { "gaura", new[] { "gaura", "gauranga", "gaurāṅga" } },
            { "nimai", new[] { "nimai", "nimāi" } },
            { "haridas", new[] { "haridas", "haridāsa" } },
            { "haridāsa", new[] { "haridāsa", "haridas" } },
            { "bhaktivinoda", new[] { "bhaktivinoda", "bhaktivinode" } },
            { "bhaktisiddhanta", new[] { "bhaktisiddhanta", "bhaktisiddhānta" } },
            { "madhava", new[] { "madhava", "mādhava" } },
            { "govinda", new[] { "govinda" } },
        };

        private static readonly Dictionary<string, string[]> DevanagariExpansions = new()
        {
            { "अर्जुन", new[] { "अर्जुन", "अजुर्न" } },
            { "धर्मक्षेत्रे", new[] { "धर्मक्षेत्रे", "धमर्क्षेत्र्ाे", "धमर्क्षेत्रो" } },
        };

        /// <summary>
        /// Parses and sanitizes a raw user query string for FTS5 execution.
        /// Returns an empty string if the query contains no valid search tokens.
        /// </summary>
        public static string Parse(string? rawQuery, bool isExactWord = false)
        {
            if (string.IsNullOrWhiteSpace(rawQuery)) return string.Empty;

            string query = rawQuery.Trim();

            // 1. If query contains Devanagari, normalize it and check known internal cluster variants
            if (ContainsDevanagari(query))
            {
                query = DevanagariNormalizer.Normalize(query);
                foreach (var kvp in DevanagariExpansions)
                {
                    if (query.Contains(kvp.Key))
                    {
                        string expanded = "(" + string.Join(" OR ", kvp.Value.Select(v => $"\"{v}\"")) + ")";
                        query = query.Replace(kvp.Key, expanded);
                    }
                }
                return query;
            }

            // 2. Citation Pattern Detection: "Bg 1.1", "SB 1.1.1", "Cc Adi 1.1", "Adi 1.1", "Noi 1", "Iso 1", "BG-1-1"
            var noiMatch = Regex.Match(query, @"^Noi\s*(\d+)$", RegexOptions.IgnoreCase);
            if (noiMatch.Success) return $"(noi AND \"{noiMatch.Groups[1].Value}\")";

            var isoMatch = Regex.Match(query, @"^Iso\s*(\d+)$", RegexOptions.IgnoreCase);
            if (isoMatch.Success) return $"(iso AND \"{isoMatch.Groups[1].Value}\")";

            var ccLilaMatch = Regex.Match(query, @"^Cc\s+(Adi|Madhya|Antya)\s*([\d\.\-]+)$", RegexOptions.IgnoreCase);
            if (ccLilaMatch.Success) return $"\"{ccLilaMatch.Groups[1].Value} {ccLilaMatch.Groups[2].Value}\"";

            var citationMatch = Regex.Match(query, @"^(?:Bg|BG|SB|Adi|Madhya|Antya|Bs|BS)\s*[\d\.\-]+$", RegexOptions.IgnoreCase);
            if (citationMatch.Success)
            {
                return $"\"{query.Replace("\"", "\"\"")}\"";
            }

            // 3. Proximity Search: Folio syntax "term1 w/N term2" or "term1 near/N term2"
            query = Regex.Replace(query, @"([\p{L}\p{N}""\-]+)\s+(?:w|near)/(\d+)\s+([\p{L}\p{N}""\-]+)", m =>
            {
                string t1 = m.Groups[1].Value.Trim('"');
                string dist = m.Groups[2].Value;
                string t2 = m.Groups[3].Value.Trim('"');
                return $"NEAR(\"{t1}\" \"{t2}\", {dist})";
            }, RegexOptions.IgnoreCase);

            // If whole query is a NEAR(...) expression, return as is
            if (Regex.IsMatch(query, @"^NEAR\s*\(.+\)$", RegexOptions.IgnoreCase))
            {
                return query;
            }

            // 4. User explicitly wrapped whole query in balanced quotes (exact phrase search)
            if (query.StartsWith("\"") && query.EndsWith("\"") && query.Length >= 2)
            {
                string inner = query.Substring(1, query.Length - 2).Replace("\"", "\"\"");
                return $"\"{inner}\"";
            }

            // Balance quotes if user left an unclosed double quote
            int quoteCount = query.Count(c => c == '"');
            if (quoteCount % 2 != 0)
            {
                query = query + "\"";
            }

            // 5. Token extraction: proximity expressions, quoted phrases, or individual words
            var tokenMatches = Regex.Matches(query, @"(?i:NEAR\s*\([^)]+\))|""[^""]+""|[\p{L}\p{N}\p{M}]+(?:[-.][\p{L}\p{N}\p{M}]+)*|[^\s]+");
            var resultTerms = new List<string>();

            foreach (Match match in tokenMatches)
            {
                string token = match.Value.Trim();
                if (string.IsNullOrEmpty(token)) continue;

                // NEAR expression: preserve intact
                if (token.StartsWith("NEAR(", StringComparison.OrdinalIgnoreCase) && token.EndsWith(")"))
                {
                    resultTerms.Add(token);
                    continue;
                }

                // Quoted phrase: preserve intact
                if (token.StartsWith("\"") && token.EndsWith("\"") && token.Length >= 2)
                {
                    resultTerms.Add(token);
                    continue;
                }

                // Support explicit uppercase boolean operators (AND, OR, NOT) between terms
                if (token == "AND" || token == "OR" || token == "NOT")
                {
                    if (resultTerms.Count > 0 && !resultTerms.Last().Equals("AND") && !resultTerms.Last().Equals("OR") && !resultTerms.Last().Equals("NOT"))
                    {
                        resultTerms.Add(token);
                    }
                    continue;
                }

                // Disarm lowercase boolean operators if standing alone
                if (token.Equals("and", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("or", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("not", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Strip leading wildcards (unsupported by SQLite FTS5)
                while (token.StartsWith("*")) token = token.Substring(1);
                if (string.IsNullOrEmpty(token)) continue;

                // Check phonetic / transliteration expansions
                string cleanWord = Regex.Replace(token, @"[^\p{L}\p{N}]", "");
                if (!string.IsNullOrEmpty(cleanWord) && PhoneticExpansions.TryGetValue(cleanWord, out var expansions))
                {
                    string orGroup = "(" + string.Join(" OR ", expansions.Select(e => $"\"{e}\"")) + ")";
                    resultTerms.Add(orGroup);
                    continue;
                }

                // If exact word is requested, or token contains punctuation like hyphen or dot, quote it
                if (isExactWord || token.Contains('-') || token.Contains('.') || token.Contains(':') || token.Contains('/'))
                {
                    resultTerms.Add($"\"{token.Replace("\"", "\"\"")}\"");
                }
                else
                {
                    resultTerms.Add(token);
                }
            }

            // Remove trailing boolean operator if query ends with one
            while (resultTerms.Count > 0 && (resultTerms.Last() == "AND" || resultTerms.Last() == "OR" || resultTerms.Last() == "NOT"))
            {
                resultTerms.RemoveAt(resultTerms.Count - 1);
            }

            if (resultTerms.Count == 0) return string.Empty;
            return string.Join(" ", resultTerms);
        }

        private static bool ContainsDevanagari(string text)
        {
            foreach (char c in text)
            {
                if (c >= '\u0900' && c <= '\u097F') return true;
            }
            return false;
        }
    }
}
