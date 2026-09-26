using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace VedaBaseModern.Core.Registry
{
    /// <summary>
    /// Production implementation of IBookRegistry.
    /// Combines canonical Prabhupāda literary metadata with live corpus database discovery.
    /// </summary>
    public class BookRegistry : IBookRegistry
    {
        private const string ProbhupadaAuthor = "His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda";

        private static readonly List<BookDescriptor> CanonicalPrabhupadaBooks = new()
        {
            new BookDescriptor { BookKey = "BG", Title = "Bhagavad-gītā As It Is", Author = ProbhupadaAuthor, Abbreviation = "Bg", Category = "Scripture", CanonicalOrder = 1 },
            new BookDescriptor { BookKey = "SB", Title = "Śrīmad-Bhāgavatam", Author = ProbhupadaAuthor, Abbreviation = "SB", Category = "Scripture", CanonicalOrder = 2 },
            new BookDescriptor { BookKey = "DI", Title = "Śrī Caitanya-caritāmṛta — Ādi-līlā", Author = ProbhupadaAuthor, Abbreviation = "CC Ādi", Category = "Scripture", CanonicalOrder = 3 },
            new BookDescriptor { BookKey = "MADHYA", Title = "Śrī Caitanya-caritāmṛta — Madhya-līlā", Author = ProbhupadaAuthor, Abbreviation = "CC Madhya", Category = "Scripture", CanonicalOrder = 4 },
            new BookDescriptor { BookKey = "ANTYA", Title = "Śrī Caitanya-caritāmṛta — Antya-līlā", Author = ProbhupadaAuthor, Abbreviation = "CC Antya", Category = "Scripture", CanonicalOrder = 5 },
            new BookDescriptor { BookKey = "NOD", Title = "The Nectar of Devotion", Author = ProbhupadaAuthor, Abbreviation = "NoD", Category = "Books", CanonicalOrder = 6 },
            new BookDescriptor { BookKey = "TLC", Title = "Teachings of Lord Caitanya", Author = ProbhupadaAuthor, Abbreviation = "TLC", Category = "Books", CanonicalOrder = 7 },
            new BookDescriptor { BookKey = "KB", Title = "Kṛṣṇa, the Supreme Personality of Godhead", Author = ProbhupadaAuthor, Abbreviation = "KB", Category = "Books", CanonicalOrder = 8 },
            new BookDescriptor { BookKey = "ISO", Title = "Śrī Īśopaniṣad", Author = ProbhupadaAuthor, Abbreviation = "Iso", Category = "Scripture", CanonicalOrder = 9 },
            new BookDescriptor { BookKey = "NOI", Title = "The Nectar of Instruction", Author = ProbhupadaAuthor, Abbreviation = "NoI", Category = "Scripture", CanonicalOrder = 10 },
            new BookDescriptor { BookKey = "TLK", Title = "Teachings of Lord Kapila", Author = ProbhupadaAuthor, Abbreviation = "TLK", Category = "Scripture", CanonicalOrder = 11 },
            new BookDescriptor { BookKey = "TQK", Title = "Teachings of Queen Kuntī", Author = ProbhupadaAuthor, Abbreviation = "TQK", Category = "Books", CanonicalOrder = 12 },
            new BookDescriptor { BookKey = "BS", Title = "Śrī Brahma-saṁhitā", Author = ProbhupadaAuthor, Abbreviation = "Bs", Category = "Scripture", CanonicalOrder = 13 },
            new BookDescriptor { BookKey = "MM", Title = "Mukunda-mālā-stotra", Author = ProbhupadaAuthor, Abbreviation = "MM", Category = "Scripture", CanonicalOrder = 14 },
            new BookDescriptor { BookKey = "NBS", Title = "Nārada-bhakti-sūtra", Author = ProbhupadaAuthor, Abbreviation = "NBS", Category = "Scripture", CanonicalOrder = 15 },
            new BookDescriptor { BookKey = "BB", Title = "Bṛhad-bhāgavatāmṛta", Author = ProbhupadaAuthor, Abbreviation = "BB", Category = "Scripture", CanonicalOrder = 16 },
            new BookDescriptor { BookKey = "SPS", Title = "Śrīla Prabhupāda Ślokas", Author = ProbhupadaAuthor, Abbreviation = "SPS", Category = "Other Works", CanonicalOrder = 17 },
            new BookDescriptor { BookKey = "DS", Title = "Dialectical Spiritualism", Author = ProbhupadaAuthor, Abbreviation = "DS", Category = "Philosophy", CanonicalOrder = 18 },
            new BookDescriptor { BookKey = "BBD", Title = "Beyond Birth and Death", Author = ProbhupadaAuthor, Abbreviation = "BBD", Category = "Books", CanonicalOrder = 18 },
            new BookDescriptor { BookKey = "POY", Title = "The Perfection of Yoga", Author = ProbhupadaAuthor, Abbreviation = "PoY", Category = "Books", CanonicalOrder = 19 },
            new BookDescriptor { BookKey = "RV", Title = "Rāja-Vidyā: The King of Knowledge", Author = ProbhupadaAuthor, Abbreviation = "RV", Category = "Books", CanonicalOrder = 20 },
            new BookDescriptor { BookKey = "EKC", Title = "Elevation to Kṛṣṇa Consciousness", Author = ProbhupadaAuthor, Abbreviation = "EKC", Category = "Books", CanonicalOrder = 21 },
            new BookDescriptor { BookKey = "KCTYS", Title = "Kṛṣṇa Consciousness: The Topmost Yoga System", Author = ProbhupadaAuthor, Abbreviation = "KCTYS", Category = "Books", CanonicalOrder = 22 },
            new BookDescriptor { BookKey = "MOG", Title = "Message of Godhead", Author = ProbhupadaAuthor, Abbreviation = "MoG", Category = "Books", CanonicalOrder = 23 },
            new BookDescriptor { BookKey = "LOB", Title = "Light of the Bhāgavata", Author = ProbhupadaAuthor, Abbreviation = "LoB", Category = "Scripture", CanonicalOrder = 24 },
            new BookDescriptor { BookKey = "PQPA", Title = "Perfect Questions, Perfect Answers", Author = ProbhupadaAuthor, Abbreviation = "PQPA", Category = "Conversations", CanonicalOrder = 25 },
            new BookDescriptor { BookKey = "SSR", Title = "The Science of Self-Realization", Author = ProbhupadaAuthor, Abbreviation = "SSR", Category = "Essays & Articles", CanonicalOrder = 26 },
            new BookDescriptor { BookKey = "JSD", Title = "The Journey of Self-Discovery", Author = ProbhupadaAuthor, Abbreviation = "JSD", Category = "Essays & Articles", CanonicalOrder = 27 },
            new BookDescriptor { BookKey = "LCFL", Title = "Life Comes From Life", Author = ProbhupadaAuthor, Abbreviation = "LCFL", Category = "Conversations", CanonicalOrder = 28 },
            new BookDescriptor { BookKey = "CB", Title = "Coming Back: The Science of Reincarnation", Author = ProbhupadaAuthor, Abbreviation = "CB", Category = "Books", CanonicalOrder = 29 },
            new BookDescriptor { BookKey = "CAT", Title = "Civilization and Transcendence", Author = ProbhupadaAuthor, Abbreviation = "CAT", Category = "Books", CanonicalOrder = 30 },
            new BookDescriptor { BookKey = "OWK", Title = "On the Way to Kṛṣṇa", Author = ProbhupadaAuthor, Abbreviation = "OWK", Category = "Books", CanonicalOrder = 31 },
            new BookDescriptor { BookKey = "SFL", Title = "The Search for Liberation", Author = ProbhupadaAuthor, Abbreviation = "SFL", Category = "Conversations", CanonicalOrder = 32 },
            new BookDescriptor { BookKey = "TT", Title = "Transcendental Teachings of Prahlāda Mahārāja", Author = ProbhupadaAuthor, Abbreviation = "TT", Category = "Books", CanonicalOrder = 33 },
            new BookDescriptor { BookKey = "EJ", Title = "Easy Journey to Other Planets", Author = ProbhupadaAuthor, Abbreviation = "EJ", Category = "Books", CanonicalOrder = 34 },
            new BookDescriptor { BookKey = "SC", Title = "A Second Chance", Author = ProbhupadaAuthor, Abbreviation = "SC", Category = "Books", CanonicalOrder = 35 },
            new BookDescriptor { BookKey = "DWT", Title = "Dharma: The Way of Transcendence", Author = ProbhupadaAuthor, Abbreviation = "DWT", Category = "Books", CanonicalOrder = 38 },
            new BookDescriptor { BookKey = "POP", Title = "Path of Perfection", Author = ProbhupadaAuthor, Abbreviation = "PoP", Category = "Books", CanonicalOrder = 39 },
            new BookDescriptor { BookKey = "QFE", Title = "Quest for Enlightenment", Author = ProbhupadaAuthor, Abbreviation = "QFE", Category = "Books", CanonicalOrder = 40 },
            new BookDescriptor { BookKey = "RTW", Title = "Renunciation Through Wisdom", Author = ProbhupadaAuthor, Abbreviation = "RTW", Category = "Books", CanonicalOrder = 41 },
            new BookDescriptor { BookKey = "LON", Title = "The Laws of Nature: An Infallible Justice", Author = ProbhupadaAuthor, Abbreviation = "LoN", Category = "Books", CanonicalOrder = 42 },
            new BookDescriptor { BookKey = "MG", Title = "Matchless Gift", Author = ProbhupadaAuthor, Abbreviation = "MG", Category = "Books", CanonicalOrder = 43 },
            new BookDescriptor { BookKey = "ROP", Title = "Reservoir of Pleasure", Author = ProbhupadaAuthor, Abbreviation = "RoP", Category = "Books", CanonicalOrder = 44 },
            new BookDescriptor { BookKey = "GG", Title = "Gītār Gāna", Author = ProbhupadaAuthor, Abbreviation = "GG", Category = "Scripture", CanonicalOrder = 45 },
            new BookDescriptor { BookKey = "SPL", Title = "Śrīla Prabhupāda-līlāmṛta", Author = "Satsvarūpa dāsa Goswami", Abbreviation = "SPL", Category = "Biographies", CanonicalOrder = 46 },
            new BookDescriptor { BookKey = "BTG", Title = "Back to Godhead (1944–1960)", Author = ProbhupadaAuthor, Abbreviation = "BTG", Category = "Essays & Articles", CanonicalOrder = 47 },
            new BookDescriptor { BookKey = "SVA", Title = "Songs of the Vaiṣṇava Ācāryas", Author = "Vaiṣṇava Ācāryas", Abbreviation = "SVA", Category = "Other Works", CanonicalOrder = 48 },
            new BookDescriptor { BookKey = "TMG", Title = "Temple Mantra Guide", Author = ProbhupadaAuthor, Abbreviation = "TMG", Category = "Other Works", CanonicalOrder = 49 },
            new BookDescriptor { BookKey = "UNKNOWN", Title = "Life Comes From Life", Author = ProbhupadaAuthor, Abbreviation = "LCFL", Category = "Conversations", CanonicalOrder = 28 }
        };

        private readonly ConcurrentDictionary<string, BookDescriptor> _booksByKey = new(StringComparer.OrdinalIgnoreCase);

        public BookRegistry()
        {
            // Seed with known canonical books so metadata is immediately queryable in-memory
            foreach (var book in CanonicalPrabhupadaBooks)
            {
                _booksByKey[book.BookKey] = book;
            }
        }

        public async Task InitializeFromDatabaseAsync(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                return;

            await Task.Run(() =>
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                    conn.Open();

                    // Read distinct books and counts from Records
                    var recordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT BookKey, COUNT(*) FROM Records GROUP BY BookKey;";
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            string key = reader.GetString(0);
                            int count = reader.GetInt32(1);
                            recordCounts[key] = count;
                        }
                    }

                    // Read optional metadata from Books table
                    var dbBooks = new Dictionary<string, (string? Title, string? Abbr, string? Category, int? Order)>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        using var bCmd = conn.CreateCommand();
                        bCmd.CommandText = "SELECT BookKey, Title, Abbreviation, Category, CanonicalOrder FROM Books;";
                        using var bReader = bCmd.ExecuteReader();
                        while (bReader.Read())
                        {
                            string k = bReader.GetString(0);
                            string? t = bReader.IsDBNull(1) ? null : bReader.GetString(1);
                            string? a = bReader.IsDBNull(2) ? null : bReader.GetString(2);
                            string? c = bReader.IsDBNull(3) ? null : bReader.GetString(3);
                            int? o = bReader.IsDBNull(4) ? null : bReader.GetInt32(4);
                            dbBooks[k] = (t, a, c, o);
                        }
                    }
                    catch
                    {
                        // Books table optional, or lacks CanonicalOrder on an older schema
                        try
                        {
                            using var bCmd = conn.CreateCommand();
                            bCmd.CommandText = "SELECT BookKey, Title, Abbreviation, Category FROM Books;";
                            using var bReader = bCmd.ExecuteReader();
                            while (bReader.Read())
                            {
                                string k = bReader.GetString(0);
                                string? t = bReader.IsDBNull(1) ? null : bReader.GetString(1);
                                string? a = bReader.IsDBNull(2) ? null : bReader.GetString(2);
                                string? c = bReader.IsDBNull(3) ? null : bReader.GetString(3);
                                dbBooks[k] = (t, a, c, null);
                            }
                        }
                        catch
                        {
                            // Books table genuinely unavailable
                        }
                    }

                    // Update or add entries for all books active in Records
                    int orderCounter = 100;
                    foreach (var (key, count) in recordCounts)
                    {
                        if (_booksByKey.TryGetValue(key, out var existing))
                        {
                            existing.RecordCount = count;
                            existing.IsAvailable = true;

                            if (dbBooks.TryGetValue(key, out var dbMeta) && !string.IsNullOrWhiteSpace(dbMeta.Title))
                            {
                                // If database has a valid title, prioritize it
                                _booksByKey[key] = new BookDescriptor
                                {
                                    BookKey = existing.BookKey,
                                    CorpusId = existing.CorpusId,
                                    Title = dbMeta.Title,
                                    Author = existing.Author,
                                    Abbreviation = !string.IsNullOrWhiteSpace(dbMeta.Abbr) ? dbMeta.Abbr : existing.Abbreviation,
                                    Category = !string.IsNullOrWhiteSpace(dbMeta.Category) ? dbMeta.Category : existing.Category,
                                    CanonicalOrder = existing.CanonicalOrder,
                                    RecordCount = count,
                                    IsAvailable = true
                                };
                            }
                        }
                        else
                        {
                            // Discovered a new book in the corpus
                            string title = key;
                            string abbr = key;
                            string cat = "Other";

                            int? dbOrder = null;
                            if (dbBooks.TryGetValue(key, out var dbMeta))
                            {
                                if (!string.IsNullOrWhiteSpace(dbMeta.Title)) title = dbMeta.Title;
                                if (!string.IsNullOrWhiteSpace(dbMeta.Abbr)) abbr = dbMeta.Abbr;
                                if (!string.IsNullOrWhiteSpace(dbMeta.Category)) cat = dbMeta.Category;
                                if (dbMeta.Order.HasValue && dbMeta.Order.Value > 0) dbOrder = dbMeta.Order;
                            }

                            _booksByKey[key] = new BookDescriptor
                            {
                                BookKey = key,
                                CorpusId = CorpusRegistry.CanonicalCorpusId,
                                Title = title,
                                Author = "His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda",
                                Abbreviation = abbr,
                                Category = cat,
                                // Prefer the manifest-sourced CanonicalOrder (via the
                                // corpus DB's Books table) so newly-discovered books
                                // appear in their intended Library position instead
                                // of an arbitrary discovery-order sequence.
                                CanonicalOrder = dbOrder ?? orderCounter++,
                                RecordCount = count,
                                IsAvailable = true
                            };
                        }
                    }
                }
                catch
                {
                    // Degrades gracefully, retaining seeded canonical books
                }
            });
        }

        public IReadOnlyList<BookDescriptor> GetAllBooks(string? corpusId = null)
        {
            var query = _booksByKey.Values.AsEnumerable();
            if (!string.IsNullOrEmpty(corpusId))
            {
                query = query.Where(b => string.Equals(b.CorpusId, corpusId, StringComparison.OrdinalIgnoreCase));
            }

            return query
                .OrderBy(b => b.CanonicalOrder)
                .ThenBy(b => b.Title)
                .ToList();
        }

        public BookDescriptor? GetBook(string bookKey)
        {
            if (string.IsNullOrWhiteSpace(bookKey)) return null;
            return _booksByKey.TryGetValue(bookKey, out var desc) ? desc : null;
        }

        public string GetBookTitle(string bookKey)
        {
            if (string.IsNullOrWhiteSpace(bookKey)) return string.Empty;
            return _booksByKey.TryGetValue(bookKey, out var desc) ? desc.Title : bookKey;
        }

        public IReadOnlyList<string> GetCanonicalBookOrder()
        {
            return GetAllBooks()
                .Select(b => b.BookKey)
                .ToList();
        }

        public bool IsBookAvailable(string bookKey)
        {
            if (string.IsNullOrWhiteSpace(bookKey)) return false;
            return _booksByKey.TryGetValue(bookKey, out var desc) && desc.IsAvailable;
        }
    }
}
