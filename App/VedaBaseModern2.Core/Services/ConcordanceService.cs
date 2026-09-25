using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.Core.Services
{
    public class ConcordanceService
    {
        private readonly string _connectionString;

        public ConcordanceService(string dbPath)
        {
            _connectionString = $"Data Source={dbPath};Mode=ReadOnly";
        }

        public async Task<ConcordanceResult> LookupWordAsync(string queryWord, int maxResults = 10000)
        {
            var result = new ConcordanceResult
            {
                QueryWord = queryWord?.Trim() ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(result.QueryWord))
                return result;

            string cleanTerm = result.QueryWord.Trim();
            string ftsTerm = cleanTerm.Replace("\"", "").Trim();
            if (string.IsNullOrEmpty(ftsTerm))
                return result;

            var matches = new List<ConcordanceMatch>();

            await Task.Run(async () =>
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();

                    // Query FTS5 for Synonyms and Transliteration (true Sanskrit lemma fields)
                    // This eliminates English translations matching substrings like 'hesitate', 'dictate', etc.
                    string ftsQuery = $"{{Synonyms Transliteration}} : \"{ftsTerm}\"";

                    string sql = @"
                        SELECT c.RecordKey, c.BookKey, c.Reference, c.Transliteration, c.Synonyms, c.Translation,
                               COALESCE(b.Title, c.BookKey) AS BookTitle
                        FROM Records c
                        JOIN RecordsFts f ON c.RowId = f.rowid
                        LEFT JOIN Books b ON c.BookKey = b.BookKey
                        WHERE RecordsFts MATCH @ftsQuery
                        ORDER BY c.Sequence ASC
                        LIMIT @limit;";

                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.AddWithValue("@ftsQuery", ftsQuery);
                    command.Parameters.AddWithValue("@limit", maxResults);

                    var wordBoundaryRegex = new Regex($@"\b{Regex.Escape(cleanTerm)}\b", RegexOptions.IgnoreCase);

                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        string recordKey = reader.GetString(0);
                        string bookKey = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        string reference = reader.IsDBNull(2) ? recordKey : reader.GetString(2);
                        string translit = reader.IsDBNull(3) ? "" : reader.GetString(3);
                        string synonyms = reader.IsDBNull(4) ? "" : reader.GetString(4);
                        string translation = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        string bookTitle = reader.IsDBNull(6) ? bookKey : reader.GetString(6);

                        string field = "Synonyms";
                        string snippet = "";
                        string gloss = "";
                        bool hasMatch = false;

                        // 1. Check Synonyms first (richest linguistic data)
                        if (!string.IsNullOrEmpty(synonyms))
                        {
                            var parts = synonyms.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var rawPart in parts)
                            {
                                var part = rawPart.Trim();
                                if (string.IsNullOrEmpty(part)) continue;

                                int dashIdx = part.IndexOf('—');
                                if (dashIdx < 0) dashIdx = part.IndexOf('-');

                                string lemmaPart = dashIdx > 0 ? part.Substring(0, dashIdx) : part;

                                if (wordBoundaryRegex.IsMatch(lemmaPart))
                                {
                                    field = "Synonyms";
                                    snippet = part;
                                    if (dashIdx > 0 && dashIdx + 1 < part.Length)
                                    {
                                        gloss = part.Substring(dashIdx + 1).Trim();
                                    }
                                    hasMatch = true;
                                    break;
                                }
                            }
                        }

                        // 2. If not matched in individual synonym lemmas, check transliteration
                        if (!hasMatch && !string.IsNullOrEmpty(translit) && wordBoundaryRegex.IsMatch(translit))
                        {
                            field = "Transliteration";
                            snippet = ExtractSnippet(translit, cleanTerm);
                            hasMatch = true;
                        }

                        if (hasMatch)
                        {
                            matches.Add(new ConcordanceMatch
                            {
                                RecordKey = recordKey,
                                BookKey = bookKey,
                                BookTitle = bookTitle,
                                Reference = reference,
                                Field = field,
                                Snippet = string.IsNullOrEmpty(snippet) ? reference : snippet,
                                Gloss = gloss
                            });
                        }
                    }

                    // If no matches found from strict token FTS (e.g. unhyphenated compound Sanskrit word like dharmaksetre for dharma-ksetre)
                    if (matches.Count == 0 && cleanTerm.Length >= 3)
                    {
                        string noHyphenTerm = cleanTerm.Replace("-", "");
                        string noHyphenPattern = $"%{noHyphenTerm}%";

                        string fallbackSql = @"
                            SELECT c.RecordKey, c.BookKey, c.Reference, c.Transliteration, c.Synonyms, c.Translation,
                                   COALESCE(b.Title, c.BookKey) AS BookTitle
                            FROM Records c
                            LEFT JOIN Books b ON c.BookKey = b.BookKey
                            WHERE REPLACE(c.Synonyms, '-', '') LIKE @noHyphenPattern
                               OR REPLACE(c.Transliteration, '-', '') LIKE @noHyphenPattern
                            ORDER BY c.Sequence ASC
                            LIMIT @limit;";

                        using var fallbackCmd = connection.CreateCommand();
                        fallbackCmd.CommandText = fallbackSql;
                        fallbackCmd.Parameters.AddWithValue("@noHyphenPattern", noHyphenPattern);
                        fallbackCmd.Parameters.AddWithValue("@limit", maxResults);

                        using var fallbackReader = await fallbackCmd.ExecuteReaderAsync();
                        while (await fallbackReader.ReadAsync())
                        {
                            string recordKey = fallbackReader.GetString(0);
                            string bookKey = fallbackReader.IsDBNull(1) ? "" : fallbackReader.GetString(1);
                            string reference = fallbackReader.IsDBNull(2) ? recordKey : fallbackReader.GetString(2);
                            string translit = fallbackReader.IsDBNull(3) ? "" : fallbackReader.GetString(3);
                            string synonyms = fallbackReader.IsDBNull(4) ? "" : fallbackReader.GetString(4);
                            string bookTitle = fallbackReader.IsDBNull(6) ? bookKey : fallbackReader.GetString(6);

                            string field = "Synonyms";
                            string snippet = "";
                            string gloss = "";
                            bool hasMatch = false;

                            if (!string.IsNullOrEmpty(synonyms))
                            {
                                var parts = synonyms.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var rawPart in parts)
                                {
                                    var part = rawPart.Trim();
                                    if (string.IsNullOrEmpty(part)) continue;

                                    int dashIdx = part.IndexOf('—');
                                    if (dashIdx < 0) dashIdx = part.IndexOf('-');

                                    string lemmaPart = dashIdx > 0 ? part.Substring(0, dashIdx) : part;

                                    if (wordBoundaryRegex.IsMatch(lemmaPart) || lemmaPart.Replace("-", "").IndexOf(noHyphenTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        field = "Synonyms";
                                        snippet = part;
                                        if (dashIdx > 0 && dashIdx + 1 < part.Length)
                                        {
                                            gloss = part.Substring(dashIdx + 1).Trim();
                                        }
                                        hasMatch = true;
                                        break;
                                    }
                                }
                            }

                            if (!hasMatch && !string.IsNullOrEmpty(translit) &&
                                (wordBoundaryRegex.IsMatch(translit) || translit.Replace("-", "").IndexOf(noHyphenTerm, StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                field = "Transliteration";
                                snippet = ExtractSnippet(translit, cleanTerm);
                                hasMatch = true;
                            }

                            if (hasMatch)
                            {
                                matches.Add(new ConcordanceMatch
                                {
                                    RecordKey = recordKey,
                                    BookKey = bookKey,
                                    BookTitle = bookTitle,
                                    Reference = reference,
                                    Field = field,
                                    Snippet = string.IsNullOrEmpty(snippet) ? reference : snippet,
                                    Gloss = gloss
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Concordance] Lookup Error: {ex}");
                }
            });

            result.AllMatches = matches;
            result.TotalCount = matches.Count;

            // Group matches by book
            var groups = matches
                .GroupBy(m => m.BookTitle)
                .Select(g => new ConcordanceBookGroup
                {
                    BookTitle = g.Key,
                    BookKey = g.First().BookKey,
                    Matches = g.ToList()
                })
                .OrderByDescending(g => g.Count)
                .ToList();

            result.BookGroups = groups;
            return result;
        }

        private static string ExtractSnippet(string fullText, string term, int contextChars = 50)
        {
            if (string.IsNullOrEmpty(fullText)) return string.Empty;
            int idx = fullText.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return fullText.Length > 100 ? fullText.Substring(0, 100) + "..." : fullText;

            int start = Math.Max(0, idx - contextChars);
            int end = Math.Min(fullText.Length, idx + term.Length + contextChars);

            string snippet = fullText.Substring(start, end - start).Trim();
            if (start > 0) snippet = "..." + snippet;
            if (end < fullText.Length) snippet = snippet + "...";
            return snippet;
        }
    }
}
