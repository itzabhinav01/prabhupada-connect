using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Repositories
{
    public class SqliteUserRepository : IUserRepository
    {
        private readonly string _connectionString;
        public string DatabasePath { get; }

        public SqliteUserRepository(string dbPath)
        {
            DatabasePath = dbPath;
            _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate";
        }

        // Current schema version. Each entry in Migrations is applied, in order,
        // starting from whatever PRAGMA user_version the existing database
        // reports - never re-applied, never applied out of order. See
        // MILESTONE_5_HISTORY_ARCHITECTURE.md section 3 for the migration table.
        private const int CurrentSchemaVersion = 14;

        private static readonly (int ToVersion, string Sql)[] Migrations = new[]
        {
            // 0 -> 1 (Milestone 4): Bookmarks + Notes. Byte-for-byte the same DDL
            // Milestone 4 originally ran, so a brand-new install still gets exactly
            // what it got before.
            (1, @"
                CREATE TABLE IF NOT EXISTS Bookmarks (
                    RecordKey TEXT PRIMARY KEY,
                    CreatedUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Notes (
                    Id TEXT PRIMARY KEY,
                    RecordKey TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_notes_recordkey ON Notes(RecordKey);
            "),

            // 1 -> 2 (Milestone 5): ReadingHistory. Purely additive - never touches
            // Bookmarks or Notes.
            (2, @"
                CREATE TABLE IF NOT EXISTS ReadingHistory (
                    RecordKey TEXT PRIMARY KEY,
                    LastOpenedUtc TEXT NOT NULL,
                    OpenCount INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS idx_readinghistory_lastopened ON ReadingHistory(LastOpenedUtc);
            "),

            // 2 -> 3 (Milestone 6): UserSettings - a single-row table (Id=1) of
            // reading-experience preferences. Purely additive - never touches
            // Bookmarks, Notes, or ReadingHistory. Seeded with the documented
            // defaults (see MILESTONE_6_SETTINGS_ARCHITECTURE.md) via INSERT OR
            // IGNORE so re-running this step (which never happens, since it's
            // gated by user_version, but defensively) can't duplicate the row.
            (3, @"
                CREATE TABLE IF NOT EXISTS UserSettings (
                    Id               INTEGER PRIMARY KEY CHECK (Id = 1),
                    Theme            TEXT NOT NULL DEFAULT 'System',
                    FontSize         TEXT NOT NULL DEFAULT 'Medium',
                    ReadingWidth     TEXT NOT NULL DEFAULT 'Comfortable',
                    LineSpacing      TEXT NOT NULL DEFAULT 'Comfortable',
                    FocusModeEnabled INTEGER NOT NULL DEFAULT 0
                );
                INSERT OR IGNORE INTO UserSettings (Id, Theme, FontSize, ReadingWidth, LineSpacing, FocusModeEnabled)
                VALUES (1, 'System', 'Medium', 'Comfortable', 'Comfortable', 0);
            "),

            // 3 -> 4 (Phase 2B): NotesFts - FTS5 index on user notes for composite search.
            (4, @"
                CREATE VIRTUAL TABLE IF NOT EXISTS NotesFts USING fts5(
                    RecordKey, Content, tokenize='unicode61 remove_diacritics 1'
                );
                INSERT INTO NotesFts(rowid, RecordKey, Content)
                SELECT rowid, RecordKey, Content FROM Notes;

                CREATE TRIGGER IF NOT EXISTS trg_notes_fts_insert AFTER INSERT ON Notes
                BEGIN
                    INSERT INTO NotesFts(rowid, RecordKey, Content) VALUES (new.rowid, new.RecordKey, new.Content);
                END;

                CREATE TRIGGER IF NOT EXISTS trg_notes_fts_delete AFTER DELETE ON Notes
                BEGIN
                    DELETE FROM NotesFts WHERE rowid = old.rowid;
                END;

                CREATE TRIGGER IF NOT EXISTS trg_notes_fts_update AFTER UPDATE ON Notes
                BEGIN
                    DELETE FROM NotesFts WHERE rowid = old.rowid;
                    INSERT INTO NotesFts(rowid, RecordKey, Content) VALUES (new.rowid, new.RecordKey, new.Content);
                END;
            "),

            // 4 -> 5 (Phase 4): per-section reading visibility toggles (Show
            // Transliteration / Synonyms / Purport). Purely additive columns on
            // the existing single-row UserSettings table, defaulted to 1 (shown)
            // so every existing installation's Reading View looks identical
            // immediately after upgrading - hiding a section is an opt-in choice,
            // never a silent behavior change.
            (5, @"
                ALTER TABLE UserSettings ADD COLUMN ShowTransliteration INTEGER NOT NULL DEFAULT 1;
                ALTER TABLE UserSettings ADD COLUMN ShowSynonyms INTEGER NOT NULL DEFAULT 1;
                ALTER TABLE UserSettings ADD COLUMN ShowPurport INTEGER NOT NULL DEFAULT 1;
            "),

            // 5 -> 6 (Phase 4.6): Bookmark Collections + Highlights. Both purely
            // additive - never touches Notes, ReadingHistory, or UserSettings.
            //
            // BookmarkCollections is a user-defined grouping ("Daily Reading",
            // "Research - Bhakti", etc). Bookmarks.CollectionId is nullable (a
            // bookmark not yet filed into any collection stays NULL - "not
            // categorized", not an error) and intentionally NOT a foreign key
            // with ON DELETE CASCADE: deleting a collection must not delete the
            // bookmarks in it (per the brief: "delete safely" - the underlying
            // bookmarks survive, they just become uncategorized again). See
            // DeleteCollectionAsync for the explicit UPDATE-then-DELETE that
            // enforces this.
            //
            // Highlights are a genuinely new content-layer table. A highlight
            // never stores or duplicates scripture text - only a stable
            // (RecordKey, Field) anchor plus a Color. 'Field' identifies WHICH
            // displayed block is highlighted (e.g. 'Translation', 'Synonyms',
            // or 'Purport:0'/'Purport:1'/... for individual purport paragraphs)
            // - block-level anchoring, not arbitrary character-offset ranges;
            // see HighlightService.md / the Phase 4.6 report for why this is
            // the deliberately-scoped, verifiable anchor granularity for this
            // phase rather than a from-scratch text-selection-offset system.
            (6, @"
                CREATE TABLE IF NOT EXISTS BookmarkCollections (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );
                ALTER TABLE Bookmarks ADD COLUMN CollectionId TEXT NULL;
                ALTER TABLE Bookmarks ADD COLUMN Title TEXT NULL;
                CREATE INDEX IF NOT EXISTS idx_bookmarks_collectionid ON Bookmarks(CollectionId);

                CREATE TABLE IF NOT EXISTS Highlights (
                    Id TEXT PRIMARY KEY,
                    RecordKey TEXT NOT NULL,
                    Field TEXT NOT NULL,
                    Color TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    UNIQUE(RecordKey, Field)
                );
                CREATE INDEX IF NOT EXISTS idx_highlights_recordkey ON Highlights(RecordKey);
            "),

            // 6 -> 7 (Phase 4.7): precision character-offset highlighting,
            // replacing whole-block highlighting. Real user testing found
            // block-level highlighting did not satisfy the actual research
            // workflow ("select a phrase, highlight only that phrase").
            //
            // Two schema changes, both handled in this one step:
            //   1. Add StartOffset/Length/SelectedText columns.
            //   2. Rebuild the table WITHOUT the old UNIQUE(RecordKey, Field)
            //      constraint - a single verse can now hold many highlights in
            //      the same field (e.g. two different highlighted phrases
            //      inside one Purport), which the old one-highlight-per-field
            //      constraint would have rejected.
            //
            // SQLite has no ALTER TABLE DROP CONSTRAINT, so this uses the
            // standard SQLite table-rebuild pattern: create the new shape,
            // copy every existing row across, drop the old table, rename.
            // Every pre-existing (Phase 4.6, block-level) highlight is
            // preserved - NEVER deleted - as a "legacy" row with the sentinel
            // StartOffset = -1, Length = -1, SelectedText = NULL. The
            // application layer (HighlightsViewModel/ReadingViewModel) treats
            // StartOffset < 0 as "legacy whole-block highlight, no precise
            // range known" and renders/handles it accordingly (still listed,
            // still removable, still color-changeable - just not re-anchored
            // to an exact phrase, since that information was never captured
            // under the old model and cannot be reconstructed after the fact
            // without guessing). This is the "retain them as block-level
            // legacy annotations" strategy - no user data is lost.
            (7, @"
                CREATE TABLE Highlights_v2 (
                    Id TEXT PRIMARY KEY,
                    RecordKey TEXT NOT NULL,
                    Field TEXT NOT NULL,
                    Color TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    StartOffset INTEGER NOT NULL DEFAULT -1,
                    Length INTEGER NOT NULL DEFAULT -1,
                    SelectedText TEXT NULL
                );
                INSERT INTO Highlights_v2 (Id, RecordKey, Field, Color, CreatedUtc, StartOffset, Length, SelectedText)
                SELECT Id, RecordKey, Field, Color, CreatedUtc, -1, -1, NULL FROM Highlights;
                DROP TABLE Highlights;
                ALTER TABLE Highlights_v2 RENAME TO Highlights;
                CREATE INDEX IF NOT EXISTS idx_highlights_recordkey ON Highlights(RecordKey);
            "),

            // 7 -> 8 (Phase 4.9.4): HighlightsFts - FTS5 index over precision
            // highlights' SelectedText, letting a highlighted phrase surface in
            // global search (UnifiedSearchService), exactly mirroring the
            // existing NotesFts pattern (same tokenizer, same trigger shape).
            // Only rows with a real SelectedText are indexed - a legacy
            // (Phase 4.6) block-level highlight has SelectedText = NULL and
            // nothing to full-text-search, so it is correctly never indexed
            // (still fully visible/manageable in the Highlights workspace,
            // just not a global-search match target, which matches reality:
            // there is no captured phrase to match against).
            (8, @"
                CREATE VIRTUAL TABLE IF NOT EXISTS HighlightsFts USING fts5(
                    RecordKey, SelectedText, tokenize='unicode61 remove_diacritics 1'
                );
                INSERT INTO HighlightsFts(rowid, RecordKey, SelectedText)
                SELECT rowid, RecordKey, SelectedText FROM Highlights WHERE SelectedText IS NOT NULL;

                CREATE TRIGGER IF NOT EXISTS trg_highlights_fts_insert AFTER INSERT ON Highlights
                WHEN new.SelectedText IS NOT NULL
                BEGIN
                    INSERT INTO HighlightsFts(rowid, RecordKey, SelectedText) VALUES (new.rowid, new.RecordKey, new.SelectedText);
                END;

                CREATE TRIGGER IF NOT EXISTS trg_highlights_fts_delete AFTER DELETE ON Highlights
                WHEN old.SelectedText IS NOT NULL
                BEGIN
                    DELETE FROM HighlightsFts WHERE rowid = old.rowid;
                END;

                CREATE TRIGGER IF NOT EXISTS trg_highlights_fts_update AFTER UPDATE ON Highlights
                BEGIN
                    DELETE FROM HighlightsFts WHERE rowid = old.rowid;
                    INSERT INTO HighlightsFts(rowid, RecordKey, SelectedText)
                    SELECT new.rowid, new.RecordKey, new.SelectedText WHERE new.SelectedText IS NOT NULL;
                END;
            "),

            // 8 -> 9 (Research Platform Phase 1): makes personal records
            // stable, timestamped and non-destructively deletable, in
            // preparation for future sync. Purely additive/preserving -
            // no existing Bookmark, Highlight, Note, Collection or Setting
            // is ever lost. NOT a SyncMetadata table yet - that is deferred
            // until the backup/sync protocol itself is designed.
            //
            // Highlights: add UpdatedUtc/DeletedUtc (additive columns).
            // Existing rows get UpdatedUtc = CreatedUtc, DeletedUtc = NULL
            // (nothing was ever soft-deleted before this migration, since
            // deletion used to be a hard DELETE). The FTS update trigger is
            // recreated (SQLite has no ALTER TRIGGER) so a highlight that
            // transitions to tombstoned (DeletedUtc set via UPDATE, not
            // DELETE) is correctly dropped from HighlightsFts - otherwise a
            // "deleted" highlight would remain findable via search forever.
            //
            // Notes: table rebuild (not just additive columns) because
            // RecordKey itself must become nullable - Phase 2 explicitly
            // requires a "General Research Note" with no scripture
            // association, which the pre-migration NOT NULL RecordKey
            // cannot represent, and SQLite cannot relax an existing NOT
            // NULL constraint via ALTER TABLE. Title/Field/StartOffset/
            // Length/DeletedUtc are all nullable so every existing note
            // stays perfectly valid with no backfill needed. Rebuilding the
            // table reassigns rowids, so NotesFts (which is keyed by rowid,
            // not Id) is fully rebuilt from the new table afterward -
            // skipping that step would silently desync every pre-existing
            // note's search entry from its actual row.
            //
            // Bookmarks: SQLite cannot ALTER a PRIMARY KEY, so this is a
            // table rebuild (the same pattern already used for the
            // Highlights 6->7 migration) that gives every bookmark a stable
            // Id independent of RecordKey - required because RecordKey-as-
            // identity cannot survive future multi-device sync (two devices
            // could independently bookmark the same verse and there would be
            // no way to tell those apart as distinct sync-able records).
            // RecordKey, CollectionId, Title and CreatedUtc are copied
            // byte-for-byte from the existing row - no bookmark or its
            // collection membership is lost. A partial UNIQUE index (on
            // RecordKey WHERE DeletedUtc IS NULL, not a table-level
            // constraint) preserves the exact pre-migration guarantee of "at
            // most one ACTIVE bookmark per verse" while still allowing a
            // tombstoned + a later re-added bookmark for the same verse to
            // coexist as separate rows.
            (9, @"
                ALTER TABLE Highlights ADD COLUMN UpdatedUtc TEXT NULL;
                ALTER TABLE Highlights ADD COLUMN DeletedUtc TEXT NULL;
                UPDATE Highlights SET UpdatedUtc = CreatedUtc WHERE UpdatedUtc IS NULL;

                DROP TRIGGER IF EXISTS trg_highlights_fts_update;
                CREATE TRIGGER trg_highlights_fts_update AFTER UPDATE ON Highlights
                BEGIN
                    DELETE FROM HighlightsFts WHERE rowid = old.rowid;
                    INSERT INTO HighlightsFts(rowid, RecordKey, SelectedText)
                    SELECT new.rowid, new.RecordKey, new.SelectedText
                    WHERE new.SelectedText IS NOT NULL AND new.DeletedUtc IS NULL;
                END;

                CREATE TABLE Notes_v2 (
                    Id TEXT PRIMARY KEY,
                    RecordKey TEXT NULL,
                    Title TEXT NULL,
                    Content TEXT NOT NULL,
                    Field TEXT NULL,
                    StartOffset INTEGER NULL,
                    Length INTEGER NULL,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    DeletedUtc TEXT NULL
                );
                INSERT INTO Notes_v2 (Id, RecordKey, Title, Content, Field, StartOffset, Length, CreatedUtc, UpdatedUtc, DeletedUtc)
                SELECT Id, RecordKey, NULL, Content, NULL, NULL, NULL, CreatedUtc, UpdatedUtc, NULL FROM Notes;
                DROP TABLE Notes;
                ALTER TABLE Notes_v2 RENAME TO Notes;
                CREATE INDEX IF NOT EXISTS idx_notes_recordkey ON Notes(RecordKey);

                DROP TRIGGER IF EXISTS trg_notes_fts_insert;
                DROP TRIGGER IF EXISTS trg_notes_fts_delete;
                DROP TRIGGER IF EXISTS trg_notes_fts_update;
                DELETE FROM NotesFts;
                INSERT INTO NotesFts(rowid, RecordKey, Content)
                SELECT rowid, COALESCE(RecordKey, ''), Content FROM Notes WHERE DeletedUtc IS NULL;

                CREATE TRIGGER trg_notes_fts_insert AFTER INSERT ON Notes
                BEGIN
                    INSERT INTO NotesFts(rowid, RecordKey, Content) VALUES (new.rowid, COALESCE(new.RecordKey, ''), new.Content);
                END;
                CREATE TRIGGER trg_notes_fts_delete AFTER DELETE ON Notes
                BEGIN
                    DELETE FROM NotesFts WHERE rowid = old.rowid;
                END;
                CREATE TRIGGER trg_notes_fts_update AFTER UPDATE ON Notes
                BEGIN
                    DELETE FROM NotesFts WHERE rowid = old.rowid;
                    INSERT INTO NotesFts(rowid, RecordKey, Content)
                    SELECT new.rowid, COALESCE(new.RecordKey, ''), new.Content WHERE new.DeletedUtc IS NULL;
                END;

                CREATE TABLE Bookmarks_v2 (
                    Id TEXT PRIMARY KEY,
                    RecordKey TEXT NOT NULL,
                    CollectionId TEXT NULL,
                    Title TEXT NULL,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    DeletedUtc TEXT NULL
                );
                INSERT INTO Bookmarks_v2 (Id, RecordKey, CollectionId, Title, CreatedUtc, UpdatedUtc, DeletedUtc)
                SELECT
                    lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' ||
                    lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' ||
                    lower(hex(randomblob(6))),
                    RecordKey, CollectionId, Title, CreatedUtc, CreatedUtc, NULL
                FROM Bookmarks;
                DROP TABLE Bookmarks;
                ALTER TABLE Bookmarks_v2 RENAME TO Bookmarks;
                CREATE INDEX IF NOT EXISTS idx_bookmarks_collectionid ON Bookmarks(CollectionId);
                CREATE INDEX IF NOT EXISTS idx_bookmarks_recordkey ON Bookmarks(RecordKey);
                CREATE UNIQUE INDEX IF NOT EXISTS idx_bookmarks_recordkey_active ON Bookmarks(RecordKey) WHERE DeletedUtc IS NULL;
            "),

            // 9 -> 10 (Phase 3: Personal Research Data Model Hardening): prepares
            // the local database for future offline backup/restore and multi-device
            // synchronization.
            //
            // BookmarkCollections: add UpdatedUtc and DeletedUtc. Existing rows get
            // UpdatedUtc = CreatedUtc, DeletedUtc = NULL. Deletion becomes a soft-delete
            // tombstone rather than physical DELETE so deletions propagate safely.
            //
            // UserSettings: add UpdatedUtc column to track configuration changes.
            // Existing row receives UpdatedUtc = datetime('now').
            //
            // SyncMetadata: key-value store for local device identity, sync cursors,
            // and schema versions. Seeds a persistent client-generated DeviceId if absent.
            (10, @"
                ALTER TABLE BookmarkCollections ADD COLUMN UpdatedUtc TEXT NULL;
                ALTER TABLE BookmarkCollections ADD COLUMN DeletedUtc TEXT NULL;
                UPDATE BookmarkCollections SET UpdatedUtc = CreatedUtc WHERE UpdatedUtc IS NULL;

                ALTER TABLE UserSettings ADD COLUMN UpdatedUtc TEXT NULL;
                UPDATE UserSettings SET UpdatedUtc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now') WHERE UpdatedUtc IS NULL;

                CREATE TABLE IF NOT EXISTS SyncMetadata (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );

                INSERT OR IGNORE INTO SyncMetadata (Key, Value, UpdatedUtc)
                VALUES (
                    'DeviceId',
                    lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' ||
                    lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' ||
                    lower(hex(randomblob(6))),
                    datetime('now')
                );
            "),

            // 10 -> 11 (Phase 5: Offline-First Cloud Sync Foundation):
            // Add performance indexes on UpdatedUtc for Bookmarks, BookmarkCollections,
            // Highlights, and Notes to optimize incremental sync change queries.
            (11, @"
                CREATE INDEX IF NOT EXISTS idx_bookmarks_updatedutc ON Bookmarks(UpdatedUtc);
                CREATE INDEX IF NOT EXISTS idx_collections_updatedutc ON BookmarkCollections(UpdatedUtc);
                CREATE INDEX IF NOT EXISTS idx_highlights_updatedutc ON Highlights(UpdatedUtc);
                CREATE INDEX IF NOT EXISTS idx_notes_updatedutc ON Notes(UpdatedUtc);
            "),

            // 11 -> 12: Custom Folders and Book Category Overrides
            (12, @"
                CREATE TABLE IF NOT EXISTS CustomFolders (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL UNIQUE,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS BookCategoryOverrides (
                    BookKey TEXT PRIMARY KEY,
                    Category TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
            "),

            // 12 -> 13: ShowPronunciationGuide option
            (13, @"
                ALTER TABLE UserSettings ADD COLUMN ShowPronunciationGuide INTEGER NOT NULL DEFAULT 1;
            "),

            // 13 -> 14: Custom Book Display Order
            (14, @"
                CREATE TABLE IF NOT EXISTS BookDisplayOrder (
                    BookKey TEXT PRIMARY KEY,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    UpdatedUtc TEXT NOT NULL
                );
            "),

            // 14 -> 15: Highlighting Palette Configuration
            (15, @"
                ALTER TABLE UserSettings ADD COLUMN HighlightPalette TEXT NULL;
            "),
        };

        public async Task InitializeAsync()
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                long currentVersion = GetUserVersion(conn);

                foreach (var (toVersion, sql) in Migrations)
                {
                    if (currentVersion >= toVersion) continue;

                    using var tx = conn.BeginTransaction();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();

                    // PRAGMA user_version cannot be parameterized and should be run outside transaction
                    using (var pragmaCmd = conn.CreateCommand())
                    {
                        pragmaCmd.CommandText = $"PRAGMA user_version = {toVersion};";
                        pragmaCmd.ExecuteNonQuery();
                    }
                    currentVersion = toVersion;
                }
            });
        }

        private static long GetUserVersion(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            var result = cmd.ExecuteScalar();
            return result is long l ? l : Convert.ToInt64(result);
        }

        public async Task<bool> IsBookmarkedAsync(string recordKey)
        {
            return await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM Bookmarks WHERE RecordKey = $rk AND DeletedUtc IS NULL";
                cmd.Parameters.AddWithValue("$rk", recordKey);
                var result = cmd.ExecuteScalar();
                return result != null;
            });
        }

        public async Task AddBookmarkAsync(string recordKey)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                // Already actively bookmarked - nothing to do (idempotent,
                // matches the pre-Phase-1 INSERT OR IGNORE behavior).
                using (var activeCmd = conn.CreateCommand())
                {
                    activeCmd.Transaction = tx;
                    activeCmd.CommandText = "SELECT 1 FROM Bookmarks WHERE RecordKey = $rk AND DeletedUtc IS NULL";
                    activeCmd.Parameters.AddWithValue("$rk", recordKey);
                    if (activeCmd.ExecuteScalar() != null) { tx.Commit(); return; }
                }

                string nowIso = DateTime.UtcNow.ToString("O");

                // A previously-removed bookmark for this verse: undelete the
                // most recently touched one rather than inserting a
                // duplicate row for the same RecordKey.
                int undeleted;
                using (var undeleteCmd = conn.CreateCommand())
                {
                    undeleteCmd.Transaction = tx;
                    undeleteCmd.CommandText = @"
                        UPDATE Bookmarks SET DeletedUtc = NULL, UpdatedUtc = $time
                        WHERE Id = (
                            SELECT Id FROM Bookmarks
                            WHERE RecordKey = $rk AND DeletedUtc IS NOT NULL
                            ORDER BY UpdatedUtc DESC LIMIT 1
                        )";
                    undeleteCmd.Parameters.AddWithValue("$rk", recordKey);
                    undeleteCmd.Parameters.AddWithValue("$time", nowIso);
                    undeleted = undeleteCmd.ExecuteNonQuery();
                }

                if (undeleted == 0)
                {
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = @"
                        INSERT INTO Bookmarks (Id, RecordKey, CollectionId, Title, CreatedUtc, UpdatedUtc, DeletedUtc)
                        VALUES ($id, $rk, NULL, NULL, $time, $time, NULL)";
                    insertCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                    insertCmd.Parameters.AddWithValue("$rk", recordKey);
                    insertCmd.Parameters.AddWithValue("$time", nowIso);
                    insertCmd.ExecuteNonQuery();
                }

                tx.Commit();
            });
        }

        public async Task RemoveBookmarkAsync(string recordKey)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string nowIso = DateTime.UtcNow.ToString("O");
                cmd.CommandText = "UPDATE Bookmarks SET DeletedUtc = $time, UpdatedUtc = $time WHERE RecordKey = $rk AND DeletedUtc IS NULL";
                cmd.Parameters.AddWithValue("$rk", recordKey);
                cmd.Parameters.AddWithValue("$time", nowIso);
                cmd.ExecuteNonQuery();
            });
        }

        public async Task<List<UserBookmark>> GetAllBookmarksAsync(bool includeDeleted = false)
        {
            return await Task.Run(() =>
            {
                var list = new List<UserBookmark>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string filter = includeDeleted ? "" : "WHERE DeletedUtc IS NULL ";
                cmd.CommandText = $"SELECT Id, RecordKey, CreatedUtc, UpdatedUtc, CollectionId, Title, DeletedUtc FROM Bookmarks {filter}ORDER BY CreatedUtc DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new UserBookmark
                    {
                        Id = reader.GetString(0),
                        RecordKey = reader.GetString(1),
                        CreatedUtc = DateTime.Parse(reader.GetString(2)),
                        UpdatedUtc = DateTime.Parse(reader.GetString(3)),
                        CollectionId = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Title = reader.IsDBNull(5) ? null : reader.GetString(5),
                        DeletedUtc = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6))
                    });
                }
                return list;
            });
        }

        public Task<List<UserBookmark>> GetAllBookmarksIncludingDeletedAsync() => GetAllBookmarksAsync(includeDeleted: true);

        public async Task<List<BookmarkCollection>> GetCollectionsAsync(bool includeDeleted = false)
        {
            return await Task.Run(() =>
            {
                var list = new List<BookmarkCollection>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string filter = includeDeleted ? "" : "WHERE DeletedUtc IS NULL ";
                cmd.CommandText = $"SELECT Id, Name, CreatedUtc, UpdatedUtc, DeletedUtc, SortOrder FROM BookmarkCollections {filter}ORDER BY SortOrder ASC, CreatedUtc ASC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new BookmarkCollection
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        CreatedUtc = DateTime.Parse(reader.GetString(2)),
                        UpdatedUtc = reader.IsDBNull(3) ? DateTime.Parse(reader.GetString(2)) : DateTime.Parse(reader.GetString(3)),
                        DeletedUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                        SortOrder = reader.GetInt32(5)
                    });
                }
                return list;
            });
        }

        public Task<List<BookmarkCollection>> GetAllCollectionsIncludingDeletedAsync() => GetCollectionsAsync(includeDeleted: true);

        public async Task<BookmarkCollection> CreateCollectionAsync(string name)
        {
            string trimmedName = name.Trim();
            var now = DateTime.UtcNow;
            string nowIso = now.ToString("O");

            return await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                // 1. If an active collection with this name already exists, return it
                // to maintain deterministic identity without duplicate active folders.
                using (var checkActiveCmd = conn.CreateCommand())
                {
                    checkActiveCmd.Transaction = tx;
                    checkActiveCmd.CommandText = "SELECT Id, Name, CreatedUtc, UpdatedUtc, DeletedUtc, SortOrder FROM BookmarkCollections WHERE DeletedUtc IS NULL AND LOWER(Name) = LOWER($name) LIMIT 1";
                    checkActiveCmd.Parameters.AddWithValue("$name", trimmedName);
                    using var r = checkActiveCmd.ExecuteReader();
                    if (r.Read())
                    {
                        var active = new BookmarkCollection
                        {
                            Id = r.GetString(0),
                            Name = r.GetString(1),
                            CreatedUtc = DateTime.Parse(r.GetString(2)),
                            UpdatedUtc = r.IsDBNull(3) ? DateTime.Parse(r.GetString(2)) : DateTime.Parse(r.GetString(3)),
                            DeletedUtc = null,
                            SortOrder = r.GetInt32(5)
                        };
                        tx.Commit();
                        return active;
                    }
                }

                // 2. If a tombstoned collection with this name exists, undelete it
                using (var checkTombstoneCmd = conn.CreateCommand())
                {
                    checkTombstoneCmd.Transaction = tx;
                    checkTombstoneCmd.CommandText = "SELECT Id, Name, CreatedUtc, SortOrder FROM BookmarkCollections WHERE DeletedUtc IS NOT NULL AND LOWER(Name) = LOWER($name) ORDER BY UpdatedUtc DESC LIMIT 1";
                    checkTombstoneCmd.Parameters.AddWithValue("$name", trimmedName);
                    using var r = checkTombstoneCmd.ExecuteReader();
                    if (r.Read())
                    {
                        string existingId = r.GetString(0);
                        var createdUtc = DateTime.Parse(r.GetString(2));
                        int sortOrder = r.GetInt32(3);
                        r.Close();

                        using var restoreCmd = conn.CreateCommand();
                        restoreCmd.Transaction = tx;
                        restoreCmd.CommandText = "UPDATE BookmarkCollections SET DeletedUtc = NULL, UpdatedUtc = $now WHERE Id = $id";
                        restoreCmd.Parameters.AddWithValue("$id", existingId);
                        restoreCmd.Parameters.AddWithValue("$now", nowIso);
                        restoreCmd.ExecuteNonQuery();

                        tx.Commit();
                        return new BookmarkCollection
                        {
                            Id = existingId,
                            Name = trimmedName,
                            CreatedUtc = createdUtc,
                            UpdatedUtc = now,
                            DeletedUtc = null,
                            SortOrder = sortOrder
                        };
                    }
                }

                // 3. Insert fresh collection
                int nextOrder = 0;
                using (var maxCmd = conn.CreateCommand())
                {
                    maxCmd.Transaction = tx;
                    maxCmd.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM BookmarkCollections";
                    nextOrder = Convert.ToInt32(maxCmd.ExecuteScalar());
                }

                string newId = Guid.NewGuid().ToString();
                using (var insertCmd = conn.CreateCommand())
                {
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = "INSERT INTO BookmarkCollections (Id, Name, CreatedUtc, UpdatedUtc, DeletedUtc, SortOrder) VALUES ($id, $name, $now, $now, NULL, $order)";
                    insertCmd.Parameters.AddWithValue("$id", newId);
                    insertCmd.Parameters.AddWithValue("$name", trimmedName);
                    insertCmd.Parameters.AddWithValue("$now", nowIso);
                    insertCmd.Parameters.AddWithValue("$order", nextOrder);
                    insertCmd.ExecuteNonQuery();
                }

                tx.Commit();
                return new BookmarkCollection
                {
                    Id = newId,
                    Name = trimmedName,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    DeletedUtc = null,
                    SortOrder = nextOrder
                };
            });
        }

        public async Task RenameCollectionAsync(string collectionId, string newName)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string nowIso = DateTime.UtcNow.ToString("O");
                cmd.CommandText = "UPDATE BookmarkCollections SET Name = $name, UpdatedUtc = $now WHERE Id = $id AND DeletedUtc IS NULL";
                cmd.Parameters.AddWithValue("$id", collectionId);
                cmd.Parameters.AddWithValue("$name", newName.Trim());
                cmd.Parameters.AddWithValue("$now", nowIso);
                cmd.ExecuteNonQuery();
            });
        }

        public async Task DeleteCollectionAsync(string collectionId)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();
                string nowIso = DateTime.UtcNow.ToString("O");

                // Bookmarks in the collection survive - they become uncategorized,
                // and their UpdatedUtc is advanced to nowIso so sync engines detect the change.
                using (var freeCmd = conn.CreateCommand())
                {
                    freeCmd.Transaction = tx;
                    freeCmd.CommandText = "UPDATE Bookmarks SET CollectionId = NULL, UpdatedUtc = $now WHERE CollectionId = $id AND DeletedUtc IS NULL";
                    freeCmd.Parameters.AddWithValue("$id", collectionId);
                    freeCmd.Parameters.AddWithValue("$now", nowIso);
                    freeCmd.ExecuteNonQuery();
                }

                // Soft-delete / tombstone the collection (never hard DELETE since Phase 3: Migration 10).
                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.Transaction = tx;
                    delCmd.CommandText = "UPDATE BookmarkCollections SET DeletedUtc = $now, UpdatedUtc = $now WHERE Id = $id";
                    delCmd.Parameters.AddWithValue("$id", collectionId);
                    delCmd.Parameters.AddWithValue("$now", nowIso);
                    delCmd.ExecuteNonQuery();
                }
                tx.Commit();
            });
        }

        public async Task SetBookmarkCollectionAsync(string recordKey, string? collectionId)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string nowIso = DateTime.UtcNow.ToString("O");
                cmd.CommandText = "UPDATE Bookmarks SET CollectionId = $cid, UpdatedUtc = $now WHERE RecordKey = $rk AND DeletedUtc IS NULL";
                cmd.Parameters.AddWithValue("$rk", recordKey);
                cmd.Parameters.AddWithValue("$cid", (object?)collectionId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$now", nowIso);
                cmd.ExecuteNonQuery();
            });
        }

        private static Highlight ReadHighlight(SqliteDataReader reader) => new Highlight
        {
            Id = reader.GetString(0),
            RecordKey = reader.GetString(1),
            Field = reader.GetString(2),
            Color = HighlightColorHelper.Parse(reader.GetString(3)),
            CreatedUtc = DateTime.Parse(reader.GetString(4)),
            StartOffset = reader.GetInt32(5),
            Length = reader.GetInt32(6),
            SelectedText = reader.IsDBNull(7) ? null : reader.GetString(7),
            UpdatedUtc = reader.IsDBNull(8) ? DateTime.Parse(reader.GetString(4)) : DateTime.Parse(reader.GetString(8)),
            DeletedUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9))
        };

        private const string HighlightColumns = "Id, RecordKey, Field, Color, CreatedUtc, StartOffset, Length, SelectedText, UpdatedUtc, DeletedUtc";

        public async Task<List<Highlight>> GetHighlightsAsync(string recordKey)
        {
            return await Task.Run(() =>
            {
                var list = new List<Highlight>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {HighlightColumns} FROM Highlights WHERE RecordKey = $rk AND DeletedUtc IS NULL";
                cmd.Parameters.AddWithValue("$rk", recordKey);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(ReadHighlight(reader));
                return list;
            });
        }

        public async Task<List<Highlight>> GetAllHighlightsAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<Highlight>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {HighlightColumns} FROM Highlights WHERE DeletedUtc IS NULL ORDER BY CreatedUtc DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(ReadHighlight(reader));
                return list;
            });
        }

        public async Task<List<Highlight>> GetAllHighlightsIncludingDeletedAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<Highlight>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {HighlightColumns} FROM Highlights ORDER BY CreatedUtc DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(ReadHighlight(reader));
                return list;
            });
        }

        public async Task<Highlight> AddHighlightAsync(string recordKey, string field, int startOffset, int length, string selectedText, HighlightColor color)
        {
            var h = new Highlight
            {
                Id = Guid.NewGuid().ToString(),
                RecordKey = recordKey,
                Field = field,
                StartOffset = startOffset,
                Length = length,
                SelectedText = selectedText,
                Color = color,
                CreatedUtc = DateTime.UtcNow
            };
            h.UpdatedUtc = h.CreatedUtc;
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                // No uniqueness constraint - a field can hold multiple
                // highlights (overlap rejection is enforced in
                // ReadingViewModel, before this is ever called, not here).
                cmd.CommandText = @"
                    INSERT INTO Highlights (Id, RecordKey, Field, Color, CreatedUtc, StartOffset, Length, SelectedText, UpdatedUtc, DeletedUtc)
                    VALUES ($id, $rk, $field, $color, $time, $start, $len, $text, $time, NULL)";
                cmd.Parameters.AddWithValue("$id", h.Id);
                cmd.Parameters.AddWithValue("$rk", h.RecordKey);
                cmd.Parameters.AddWithValue("$field", h.Field);
                cmd.Parameters.AddWithValue("$color", h.Color.ToString());
                cmd.Parameters.AddWithValue("$time", h.CreatedUtc.ToString("O"));
                cmd.Parameters.AddWithValue("$start", h.StartOffset);
                cmd.Parameters.AddWithValue("$len", h.Length);
                cmd.Parameters.AddWithValue("$text", h.SelectedText);
                cmd.ExecuteNonQuery();
            });
            return h;
        }

        public async Task RemoveHighlightAsync(string highlightId)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string nowIso = DateTime.UtcNow.ToString("O");
                cmd.CommandText = "UPDATE Highlights SET DeletedUtc = $time, UpdatedUtc = $time WHERE Id = $id";
                cmd.Parameters.AddWithValue("$id", highlightId);
                cmd.Parameters.AddWithValue("$time", nowIso);
                cmd.ExecuteNonQuery();
            });
        }

        public async Task UpdateHighlightColorAsync(string highlightId, HighlightColor color)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Highlights SET Color = $color, UpdatedUtc = $time WHERE Id = $id";
                cmd.Parameters.AddWithValue("$id", highlightId);
                cmd.Parameters.AddWithValue("$color", color.ToString());
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            });
        }

        public async Task<int> GetBookmarkCountAsync() => await ScalarCountAsync("SELECT COUNT(*) FROM Bookmarks WHERE DeletedUtc IS NULL");
        public async Task<int> GetNoteCountAsync() => await ScalarCountAsync("SELECT COUNT(*) FROM Notes WHERE DeletedUtc IS NULL");
        public async Task<int> GetHighlightCountAsync() => await ScalarCountAsync("SELECT COUNT(*) FROM Highlights WHERE DeletedUtc IS NULL");

        private async Task<int> ScalarCountAsync(string sql)
        {
            return await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt32(cmd.ExecuteScalar());
            });
        }

        private const string NoteColumns = "Id, RecordKey, Content, CreatedUtc, UpdatedUtc, Title, Field, StartOffset, Length, DeletedUtc";

        private static UserNote ReadNote(SqliteDataReader reader) => new UserNote
        {
            Id = reader.GetString(0),
            RecordKey = reader.IsDBNull(1) ? null : reader.GetString(1),
            Content = reader.GetString(2),
            CreatedUtc = DateTime.Parse(reader.GetString(3)),
            UpdatedUtc = DateTime.Parse(reader.GetString(4)),
            Title = reader.IsDBNull(5) ? null : reader.GetString(5),
            Field = reader.IsDBNull(6) ? null : reader.GetString(6),
            StartOffset = reader.IsDBNull(7) ? -1 : reader.GetInt32(7),
            Length = reader.IsDBNull(8) ? -1 : reader.GetInt32(8),
            DeletedUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9))
        };

        public async Task<List<UserNote>> GetNotesAsync(string recordKey)
        {
            return await Task.Run(() =>
            {
                var list = new List<UserNote>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {NoteColumns} FROM Notes WHERE RecordKey = $rk AND DeletedUtc IS NULL ORDER BY CreatedUtc ASC";
                cmd.Parameters.AddWithValue("$rk", recordKey);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(ReadNote(reader));
                return list;
            });
        }

        public async Task<List<UserNote>> GetAllNotesAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<UserNote>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {NoteColumns} FROM Notes WHERE DeletedUtc IS NULL ORDER BY UpdatedUtc DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(ReadNote(reader));
                return list;
            });
        }

        public async Task<List<UserNote>> GetAllNotesIncludingDeletedAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<UserNote>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {NoteColumns} FROM Notes ORDER BY UpdatedUtc DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) list.Add(ReadNote(reader));
                return list;
            });
        }

        public async Task<UserNote> CreateNoteAsync(string? recordKey, string content, string? title = null, string? field = null, int startOffset = -1, int length = -1)
        {
            string? normalizedTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            bool hasAnchor = !string.IsNullOrEmpty(recordKey) && !string.IsNullOrEmpty(field) && startOffset >= 0 && length > 0;

            var note = new UserNote
            {
                Id = Guid.NewGuid().ToString(),
                RecordKey = string.IsNullOrWhiteSpace(recordKey) ? null : recordKey,
                Content = content,
                Title = normalizedTitle,
                Field = hasAnchor ? field : null,
                StartOffset = hasAnchor ? startOffset : -1,
                Length = hasAnchor ? length : -1,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Notes (Id, RecordKey, Content, CreatedUtc, UpdatedUtc, Title, Field, StartOffset, Length, DeletedUtc)
                    VALUES ($id, $rk, $content, $time, $time, $title, $field, $start, $len, NULL)";
                cmd.Parameters.AddWithValue("$id", note.Id);
                cmd.Parameters.AddWithValue("$rk", (object?)note.RecordKey ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$content", note.Content);
                cmd.Parameters.AddWithValue("$time", note.CreatedUtc.ToString("O"));
                cmd.Parameters.AddWithValue("$title", (object?)note.Title ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$field", (object?)note.Field ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$start", note.Field != null ? note.StartOffset : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$len", note.Field != null ? note.Length : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            });

            return note;
        }

        public async Task UpdateNoteAsync(string id, string content, string? title = null)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string? normalizedTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
                cmd.CommandText = "UPDATE Notes SET Content = $content, Title = $title, UpdatedUtc = $time WHERE Id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$content", content);
                cmd.Parameters.AddWithValue("$title", (object?)normalizedTitle ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            });
        }

        public async Task DeleteNoteAsync(string id)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string nowIso = DateTime.UtcNow.ToString("O");
                cmd.CommandText = "UPDATE Notes SET DeletedUtc = $time, UpdatedUtc = $time WHERE Id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$time", nowIso);
                cmd.ExecuteNonQuery();
            });
        }

        // Retention cap for ReadingHistory - see MILESTONE_5_HISTORY_ARCHITECTURE.md
        // section 4 for rationale. Pruning here NEVER touches Bookmarks or Notes.
        private const int HistoryRetentionLimit = 500;

        public async Task RecordHistoryAsync(string recordKey)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                // Single atomic UPSERT - no SELECT-then-INSERT race window.
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO ReadingHistory (RecordKey, LastOpenedUtc, OpenCount)
                        VALUES ($rk, $time, 1)
                        ON CONFLICT(RecordKey) DO UPDATE SET
                            LastOpenedUtc = excluded.LastOpenedUtc,
                            OpenCount = OpenCount + 1;";
                    cmd.Parameters.AddWithValue("$rk", recordKey);
                    cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
                    cmd.ExecuteNonQuery();
                }

                // Bounded retention: keep only the most-recently-opened N distinct records.
                using (var pruneCmd = conn.CreateCommand())
                {
                    pruneCmd.Transaction = tx;
                    pruneCmd.CommandText = @"
                        DELETE FROM ReadingHistory
                        WHERE RecordKey NOT IN (
                            SELECT RecordKey FROM ReadingHistory ORDER BY LastOpenedUtc DESC LIMIT $cap
                        );";
                    pruneCmd.Parameters.AddWithValue("$cap", HistoryRetentionLimit);
                    pruneCmd.ExecuteNonQuery();
                }

                tx.Commit();
            });
        }

        public async Task<List<ReadingHistoryEntry>> GetRecentHistoryAsync(int limit = 100)
        {
            return await Task.Run(() =>
            {
                var list = new List<ReadingHistoryEntry>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT RecordKey, LastOpenedUtc, OpenCount FROM ReadingHistory ORDER BY LastOpenedUtc DESC LIMIT $limit";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ReadingHistoryEntry
                    {
                        RecordKey = reader.GetString(0),
                        LastOpenedUtc = DateTime.Parse(reader.GetString(1)),
                        OpenCount = reader.GetInt32(2)
                    });
                }
                return list;
            });
        }

        public async Task<List<ReadingHistoryEntry>> GetAllHistoryAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<ReadingHistoryEntry>();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT RecordKey, LastOpenedUtc, OpenCount FROM ReadingHistory ORDER BY LastOpenedUtc DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ReadingHistoryEntry
                    {
                        RecordKey = reader.GetString(0),
                        LastOpenedUtc = DateTime.Parse(reader.GetString(1)),
                        OpenCount = reader.GetInt32(2)
                    });
                }
                return list;
            });
        }

        public async Task ClearHistoryAsync()
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                // Only ReadingHistory. Bookmarks and Notes are separate tables and
                // are never referenced by this statement.
                cmd.CommandText = "DELETE FROM ReadingHistory";
                cmd.ExecuteNonQuery();
            });
        }

        public async Task<List<UserSearchResult>> SearchUserContentAsync(string query)
        {
            var results = new List<UserSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            string parsedQuery = Services.FtsQueryParser.Parse(query);
            if (string.IsNullOrWhiteSpace(parsedQuery)) return results;

            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                // 1. Search Notes using NotesFts
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT n.RecordKey, snippet(NotesFts, 1, '«', '»', '...', 25) as snip, n.UpdatedUtc
                        FROM NotesFts fts
                        JOIN Notes n ON n.rowid = fts.rowid
                        WHERE NotesFts MATCH $q AND n.DeletedUtc IS NULL
                        ORDER BY rank
                        LIMIT 50";
                    cmd.Parameters.AddWithValue("$q", parsedQuery);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        results.Add(new UserSearchResult
                        {
                            RecordKey = reader.IsDBNull(0) ? null : reader.GetString(0),
                            SourceType = "Note",
                            ContentSnippet = reader.GetString(1),
                            TimestampUtc = DateTime.Parse(reader.GetString(2))
                        });
                    }
                }
                catch (SqliteException)
                {
                    // Safe fallback to LIKE query if FTS table has syntax issue
                    using var fallbackCmd = conn.CreateCommand();
                    fallbackCmd.CommandText = "SELECT RecordKey, Content, UpdatedUtc FROM Notes WHERE Content LIKE $like AND DeletedUtc IS NULL LIMIT 50";
                    fallbackCmd.Parameters.AddWithValue("$like", $"%{query}%");
                    using var r = fallbackCmd.ExecuteReader();
                    while (r.Read())
                    {
                        results.Add(new UserSearchResult
                        {
                            RecordKey = r.IsDBNull(0) ? null : r.GetString(0),
                            SourceType = "Note",
                            ContentSnippet = r.GetString(1),
                            TimestampUtc = DateTime.Parse(r.GetString(2))
                        });
                    }
                }

                // 2. Search Bookmarks by RecordKey match
                try
                {
                    using var bmCmd = conn.CreateCommand();
                    bmCmd.CommandText = "SELECT RecordKey, CreatedUtc FROM Bookmarks WHERE RecordKey LIKE $like AND DeletedUtc IS NULL LIMIT 20";
                    bmCmd.Parameters.AddWithValue("$like", $"%{query.Trim().Replace(" ", "-")}%");
                    using var bmReader = bmCmd.ExecuteReader();
                    while (bmReader.Read())
                    {
                        string rk = bmReader.GetString(0);
                        if (!results.Any(x => x.RecordKey == rk && x.SourceType == "Bookmark"))
                        {
                            results.Add(new UserSearchResult
                            {
                                RecordKey = rk,
                                SourceType = "Bookmark",
                                ContentSnippet = $"Bookmarked on {DateTime.Parse(bmReader.GetString(1)).ToLocalTime():g}",
                                TimestampUtc = DateTime.Parse(bmReader.GetString(1))
                            });
                        }
                    }
                }
                catch (Exception)
                {
                    // Safe swallow
                }

                // 3. Search precision Highlights (SelectedText) using HighlightsFts.
                // Field/StartOffset/Length/Color ride along on every result so a
                // click can restore the exact source location, not just open the
                // chapter - the same (RecordKey, Field, StartOffset, Length)
                // anchor already used to paint the highlight in Reading View.
                try
                {
                    using var hlCmd = conn.CreateCommand();
                    hlCmd.CommandText = @"
                        SELECT h.RecordKey, h.Field, h.StartOffset, h.Length, h.Color, h.CreatedUtc,
                               snippet(HighlightsFts, 1, '«', '»', '...', 25) as snip
                        FROM HighlightsFts fts
                        JOIN Highlights h ON h.rowid = fts.rowid
                        WHERE HighlightsFts MATCH $q AND h.DeletedUtc IS NULL
                        ORDER BY rank
                        LIMIT 50";
                    hlCmd.Parameters.AddWithValue("$q", parsedQuery);

                    using var hlReader = hlCmd.ExecuteReader();
                    while (hlReader.Read())
                    {
                        results.Add(new UserSearchResult
                        {
                            RecordKey = hlReader.GetString(0),
                            SourceType = "Highlight",
                            Field = hlReader.GetString(1),
                            StartOffset = hlReader.GetInt32(2),
                            Length = hlReader.GetInt32(3),
                            Color = HighlightColorHelper.Parse(hlReader.GetString(4)),
                            TimestampUtc = DateTime.Parse(hlReader.GetString(5)),
                            ContentSnippet = hlReader.GetString(6)
                        });
                    }
                }
                catch (SqliteException)
                {
                    // Safe fallback to LIKE query if FTS table has a syntax issue.
                    using var fallbackCmd = conn.CreateCommand();
                    fallbackCmd.CommandText = @"
                        SELECT RecordKey, Field, StartOffset, Length, Color, CreatedUtc, SelectedText
                        FROM Highlights
                        WHERE SelectedText IS NOT NULL AND SelectedText LIKE $like AND DeletedUtc IS NULL
                        LIMIT 50";
                    fallbackCmd.Parameters.AddWithValue("$like", $"%{query}%");
                    using var r = fallbackCmd.ExecuteReader();
                    while (r.Read())
                    {
                        results.Add(new UserSearchResult
                        {
                            RecordKey = r.GetString(0),
                            SourceType = "Highlight",
                            Field = r.GetString(1),
                            StartOffset = r.GetInt32(2),
                            Length = r.GetInt32(3),
                            Color = HighlightColorHelper.Parse(r.GetString(4)),
                            TimestampUtc = DateTime.Parse(r.GetString(5)),
                            ContentSnippet = r.GetString(6)
                        });
                    }
                }
            });

            return results;
        }

        public async Task<string?> GetSyncMetadataAsync(string key)
        {
            return await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM SyncMetadata WHERE Key = $key";
                cmd.Parameters.AddWithValue("$key", key);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : result.ToString();
            });
        }

        public async Task SetSyncMetadataAsync(string key, string value)
        {
            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                string nowIso = DateTime.UtcNow.ToString("O");
                cmd.CommandText = @"
                    INSERT INTO SyncMetadata (Key, Value, UpdatedUtc)
                    VALUES ($key, $val, $now)
                    ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedUtc = excluded.UpdatedUtc;";
                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$val", value);
                cmd.Parameters.AddWithValue("$now", nowIso);
                cmd.ExecuteNonQuery();
            });
        }

        public async Task<string> GetDeviceIdAsync()
        {
            var deviceId = await GetSyncMetadataAsync("DeviceId");
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return deviceId;
            }

            string newId = Guid.NewGuid().ToString();
            await SetSyncMetadataAsync("DeviceId", newId);
            return newId;
        }

        public async Task RestoreDataAsync(BackupDataPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                // 1. Wipe all user data tables
                using (var clearCmd = conn.CreateCommand())
                {
                    clearCmd.Transaction = tx;
                    clearCmd.CommandText = @"
                        DELETE FROM Bookmarks;
                        DELETE FROM BookmarkCollections;
                        DELETE FROM Highlights;
                        DELETE FROM Notes;
                        DELETE FROM ReadingHistory;
                        DELETE FROM NotesFts;
                        DELETE FROM HighlightsFts;
                    ";
                    clearCmd.ExecuteNonQuery();
                }

                // 2. Insert collections
                if (payload.Collections != null && payload.Collections.Count > 0)
                {
                    foreach (var c in payload.Collections)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO BookmarkCollections (Id, Name, CreatedUtc, UpdatedUtc, DeletedUtc, SortOrder)
                            VALUES ($id, $name, $created, $updated, $deleted, $sort)";
                        cmd.Parameters.AddWithValue("$id", c.Id);
                        cmd.Parameters.AddWithValue("$name", c.Name);
                        cmd.Parameters.AddWithValue("$created", c.CreatedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$updated", c.UpdatedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$deleted", (object?)c.DeletedUtc?.ToString("O") ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$sort", c.SortOrder);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 3. Insert bookmarks
                if (payload.Bookmarks != null && payload.Bookmarks.Count > 0)
                {
                    foreach (var b in payload.Bookmarks)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO Bookmarks (Id, RecordKey, CollectionId, Title, CreatedUtc, UpdatedUtc, DeletedUtc)
                            VALUES ($id, $rk, $cid, $title, $created, $updated, $deleted)";
                        cmd.Parameters.AddWithValue("$id", b.Id);
                        cmd.Parameters.AddWithValue("$rk", b.RecordKey);
                        cmd.Parameters.AddWithValue("$cid", (object?)b.CollectionId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$title", (object?)b.Title ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$created", b.CreatedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$updated", b.UpdatedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$deleted", (object?)b.DeletedUtc?.ToString("O") ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 4. Insert highlights
                if (payload.Highlights != null && payload.Highlights.Count > 0)
                {
                    foreach (var h in payload.Highlights)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO Highlights (Id, RecordKey, Field, Color, CreatedUtc, StartOffset, Length, SelectedText, UpdatedUtc, DeletedUtc)
                            VALUES ($id, $rk, $field, $color, $created, $start, $len, $text, $updated, $deleted)";
                        cmd.Parameters.AddWithValue("$id", h.Id);
                        cmd.Parameters.AddWithValue("$rk", h.RecordKey);
                        cmd.Parameters.AddWithValue("$field", h.Field);
                        cmd.Parameters.AddWithValue("$color", h.Color.ToString());
                        cmd.Parameters.AddWithValue("$created", h.CreatedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$start", h.StartOffset);
                        cmd.Parameters.AddWithValue("$len", h.Length);
                        cmd.Parameters.AddWithValue("$text", (object?)h.SelectedText ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$updated", h.UpdatedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$deleted", (object?)h.DeletedUtc?.ToString("O") ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 5. Insert notes
                if (payload.Notes != null && payload.Notes.Count > 0)
                {
                    foreach (var n in payload.Notes)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO Notes (Id, RecordKey, Title, Content, Field, StartOffset, Length, CreatedUtc, UpdatedUtc, DeletedUtc)
                            VALUES ($id, $rk, $title, $content, $field, $start, $len, $created, $updated, $deleted)";
                        cmd.Parameters.AddWithValue("$id", n.Id);
                        cmd.Parameters.AddWithValue("$rk", (object?)n.RecordKey ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$title", (object?)n.Title ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$content", n.Content);
                        cmd.Parameters.AddWithValue("$field", (object?)n.Field ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$start", n.Field != null ? n.StartOffset : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("$len", n.Field != null ? n.Length : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("$created", n.CreatedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$updated", n.UpdatedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$deleted", (object?)n.DeletedUtc?.ToString("O") ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 6. Insert reading history
                if (payload.ReadingHistory != null && payload.ReadingHistory.Count > 0)
                {
                    foreach (var rh in payload.ReadingHistory)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO ReadingHistory (RecordKey, LastOpenedUtc, OpenCount)
                            VALUES ($rk, $time, $count)";
                        cmd.Parameters.AddWithValue("$rk", rh.RecordKey);
                        cmd.Parameters.AddWithValue("$time", rh.LastOpenedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$count", rh.OpenCount);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 7. Update UserSettings
                if (payload.Settings != null)
                {
                    string? paletteJson = payload.Settings.HighlightPalette != null && payload.Settings.HighlightPalette.Count > 0
                        ? JsonSerializer.Serialize(payload.Settings.HighlightPalette)
                        : null;
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO UserSettings (Id, Theme, FontSize, ReadingWidth, LineSpacing, FocusModeEnabled, ShowTransliteration, ShowSynonyms, ShowPurport, UpdatedUtc, HighlightPalette)
                        VALUES (1, $theme, $fontSize, $width, $spacing, $focus, $showTranslit, $showSyn, $showPurport, $updated, $palette)
                        ON CONFLICT(Id) DO UPDATE SET
                            Theme = excluded.Theme,
                            FontSize = excluded.FontSize,
                            ReadingWidth = excluded.ReadingWidth,
                            LineSpacing = excluded.LineSpacing,
                            FocusModeEnabled = excluded.FocusModeEnabled,
                            ShowTransliteration = excluded.ShowTransliteration,
                            ShowSynonyms = excluded.ShowSynonyms,
                            ShowPurport = excluded.ShowPurport,
                            HighlightPalette = COALESCE(excluded.HighlightPalette, UserSettings.HighlightPalette),
                            UpdatedUtc = excluded.UpdatedUtc;";
                    cmd.Parameters.AddWithValue("$theme", payload.Settings.Theme.ToString());
                    cmd.Parameters.AddWithValue("$fontSize", payload.Settings.FontSize.ToString());
                    cmd.Parameters.AddWithValue("$width", payload.Settings.ReadingWidth.ToString());
                    cmd.Parameters.AddWithValue("$spacing", payload.Settings.LineSpacing.ToString());
                    cmd.Parameters.AddWithValue("$focus", payload.Settings.FocusModeEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("$showTranslit", payload.Settings.ShowTransliteration ? 1 : 0);
                    cmd.Parameters.AddWithValue("$showSyn", payload.Settings.ShowSynonyms ? 1 : 0);
                    cmd.Parameters.AddWithValue("$showPurport", payload.Settings.ShowPurport ? 1 : 0);
                    cmd.Parameters.AddWithValue("$palette", (object?)paletteJson ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$updated", payload.Settings.UpdatedUtc.ToString("O"));
                    cmd.ExecuteNonQuery();
                }

                // 8. Cleanly re-index FTS
                using (var ftsCmd = conn.CreateCommand())
                {
                    ftsCmd.Transaction = tx;
                    ftsCmd.CommandText = @"
                        DELETE FROM NotesFts;
                        INSERT INTO NotesFts(rowid, RecordKey, Content)
                        SELECT rowid, COALESCE(RecordKey, ''), Content FROM Notes WHERE DeletedUtc IS NULL;

                        DELETE FROM HighlightsFts;
                        INSERT INTO HighlightsFts(rowid, RecordKey, SelectedText)
                        SELECT rowid, RecordKey, SelectedText FROM Highlights WHERE SelectedText IS NOT NULL AND DeletedUtc IS NULL;
                    ";
                    ftsCmd.ExecuteNonQuery();
                }

                tx.Commit();
            });
        }

        public async Task MergeDataAsync(BackupDataPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            await Task.Run(() =>
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                // 1. Collections: LWW by Id
                if (payload.Collections != null)
                {
                    foreach (var c in payload.Collections)
                    {
                        using var checkCmd = conn.CreateCommand();
                        checkCmd.Transaction = tx;
                        checkCmd.CommandText = "SELECT UpdatedUtc FROM BookmarkCollections WHERE Id = $id";
                        checkCmd.Parameters.AddWithValue("$id", c.Id);
                        var existingUpdatedObj = checkCmd.ExecuteScalar();

                        if (existingUpdatedObj == null)
                        {
                            using var ins = conn.CreateCommand();
                            ins.Transaction = tx;
                            ins.CommandText = @"
                                INSERT INTO BookmarkCollections (Id, Name, CreatedUtc, UpdatedUtc, DeletedUtc, SortOrder)
                                VALUES ($id, $name, $created, $updated, $deleted, $sort)";
                            ins.Parameters.AddWithValue("$id", c.Id);
                            ins.Parameters.AddWithValue("$name", c.Name);
                            ins.Parameters.AddWithValue("$created", c.CreatedUtc.ToString("O"));
                            ins.Parameters.AddWithValue("$updated", c.UpdatedUtc.ToString("O"));
                            ins.Parameters.AddWithValue("$deleted", (object?)c.DeletedUtc?.ToString("O") ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$sort", c.SortOrder);
                            ins.ExecuteNonQuery();
                        }
                        else
                        {
                            var existingUpdated = DateTime.Parse(existingUpdatedObj.ToString()!).ToUniversalTime();
                            if (c.UpdatedUtc >= existingUpdated)
                            {
                                using var upd = conn.CreateCommand();
                                upd.Transaction = tx;
                                upd.CommandText = @"
                                    UPDATE BookmarkCollections
                                    SET Name = $name, CreatedUtc = $created, UpdatedUtc = $updated, DeletedUtc = $deleted, SortOrder = $sort
                                    WHERE Id = $id";
                                upd.Parameters.AddWithValue("$id", c.Id);
                                upd.Parameters.AddWithValue("$name", c.Name);
                                upd.Parameters.AddWithValue("$created", c.CreatedUtc.ToString("O"));
                                upd.Parameters.AddWithValue("$updated", c.UpdatedUtc.ToString("O"));
                                upd.Parameters.AddWithValue("$deleted", (object?)c.DeletedUtc?.ToString("O") ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$sort", c.SortOrder);
                                upd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // 2. Bookmarks: LWW by Id, with uniqueness guard for active bookmarks
                if (payload.Bookmarks != null)
                {
                    foreach (var b in payload.Bookmarks)
                    {
                        using var checkCmd = conn.CreateCommand();
                        checkCmd.Transaction = tx;
                        checkCmd.CommandText = "SELECT UpdatedUtc FROM Bookmarks WHERE Id = $id";
                        checkCmd.Parameters.AddWithValue("$id", b.Id);
                        var existingUpdatedObj = checkCmd.ExecuteScalar();

                        if (existingUpdatedObj != null)
                        {
                            var existingUpdated = DateTime.Parse(existingUpdatedObj.ToString()!).ToUniversalTime();
                            if (b.UpdatedUtc >= existingUpdated)
                            {
                                // If activating this bookmark, ensure no OTHER active bookmark for same RecordKey
                                if (b.DeletedUtc == null)
                                {
                                    using var tombOther = conn.CreateCommand();
                                    tombOther.Transaction = tx;
                                    tombOther.CommandText = "UPDATE Bookmarks SET DeletedUtc = $now, UpdatedUtc = $now WHERE RecordKey = $rk AND Id != $id AND DeletedUtc IS NULL";
                                    tombOther.Parameters.AddWithValue("$rk", b.RecordKey);
                                    tombOther.Parameters.AddWithValue("$id", b.Id);
                                    tombOther.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                                    tombOther.ExecuteNonQuery();
                                }

                                using var upd = conn.CreateCommand();
                                upd.Transaction = tx;
                                upd.CommandText = @"
                                    UPDATE Bookmarks
                                    SET RecordKey = $rk, CollectionId = $cid, Title = $title, CreatedUtc = $created, UpdatedUtc = $updated, DeletedUtc = $deleted
                                    WHERE Id = $id";
                                upd.Parameters.AddWithValue("$id", b.Id);
                                upd.Parameters.AddWithValue("$rk", b.RecordKey);
                                upd.Parameters.AddWithValue("$cid", (object?)b.CollectionId ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$title", (object?)b.Title ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$created", b.CreatedUtc.ToString("O"));
                                upd.Parameters.AddWithValue("$updated", b.UpdatedUtc.ToString("O"));
                                upd.Parameters.AddWithValue("$deleted", (object?)b.DeletedUtc?.ToString("O") ?? DBNull.Value);
                                upd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            // New row. If incoming is active, check if another active bookmark exists for RecordKey
                            if (b.DeletedUtc == null)
                            {
                                using var checkActive = conn.CreateCommand();
                                checkActive.Transaction = tx;
                                checkActive.CommandText = "SELECT Id, UpdatedUtc FROM Bookmarks WHERE RecordKey = $rk AND DeletedUtc IS NULL LIMIT 1";
                                checkActive.Parameters.AddWithValue("$rk", b.RecordKey);
                                using var r = checkActive.ExecuteReader();
                                if (r.Read())
                                {
                                    string existingId = r.GetString(0);
                                    DateTime activeUpdated = DateTime.Parse(r.GetString(1)).ToUniversalTime();
                                    r.Close();

                                    if (b.UpdatedUtc >= activeUpdated)
                                    {
                                        // Incoming is newer: tombstone existing active
                                        using var tombOther = conn.CreateCommand();
                                        tombOther.Transaction = tx;
                                        tombOther.CommandText = "UPDATE Bookmarks SET DeletedUtc = $now, UpdatedUtc = $now WHERE Id = $id";
                                        tombOther.Parameters.AddWithValue("$id", existingId);
                                        tombOther.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                                        tombOther.ExecuteNonQuery();

                                        InsertBookmark(conn, tx, b);
                                    }
                                    else
                                    {
                                        // Local is newer: insert incoming as tombstoned
                                        var tombstonedCopy = new UserBookmark
                                        {
                                            Id = b.Id,
                                            RecordKey = b.RecordKey,
                                            CollectionId = b.CollectionId,
                                            Title = b.Title,
                                            CreatedUtc = b.CreatedUtc,
                                            UpdatedUtc = b.UpdatedUtc,
                                            DeletedUtc = b.UpdatedUtc
                                        };
                                        InsertBookmark(conn, tx, tombstonedCopy);
                                    }
                                }
                                else
                                {
                                    r.Close();
                                    InsertBookmark(conn, tx, b);
                                }
                            }
                            else
                            {
                                InsertBookmark(conn, tx, b);
                            }
                        }
                    }
                }

                // 3. Highlights: LWW by Id
                if (payload.Highlights != null)
                {
                    foreach (var h in payload.Highlights)
                    {
                        using var checkCmd = conn.CreateCommand();
                        checkCmd.Transaction = tx;
                        checkCmd.CommandText = "SELECT UpdatedUtc FROM Highlights WHERE Id = $id";
                        checkCmd.Parameters.AddWithValue("$id", h.Id);
                        var existingUpdatedObj = checkCmd.ExecuteScalar();

                        if (existingUpdatedObj == null)
                        {
                            using var ins = conn.CreateCommand();
                            ins.Transaction = tx;
                            ins.CommandText = @"
                                INSERT INTO Highlights (Id, RecordKey, Field, Color, CreatedUtc, StartOffset, Length, SelectedText, UpdatedUtc, DeletedUtc)
                                VALUES ($id, $rk, $field, $color, $created, $start, $len, $text, $updated, $deleted)";
                            ins.Parameters.AddWithValue("$id", h.Id);
                            ins.Parameters.AddWithValue("$rk", h.RecordKey);
                            ins.Parameters.AddWithValue("$field", h.Field);
                            ins.Parameters.AddWithValue("$color", h.Color.ToString());
                            ins.Parameters.AddWithValue("$created", h.CreatedUtc.ToString("O"));
                            ins.Parameters.AddWithValue("$start", h.StartOffset);
                            ins.Parameters.AddWithValue("$len", h.Length);
                            ins.Parameters.AddWithValue("$text", (object?)h.SelectedText ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$updated", h.UpdatedUtc.ToString("O"));
                            ins.Parameters.AddWithValue("$deleted", (object?)h.DeletedUtc?.ToString("O") ?? DBNull.Value);
                            ins.ExecuteNonQuery();
                        }
                        else
                        {
                            var existingUpdated = DateTime.Parse(existingUpdatedObj.ToString()!).ToUniversalTime();
                            if (h.UpdatedUtc >= existingUpdated)
                            {
                                using var upd = conn.CreateCommand();
                                upd.Transaction = tx;
                                upd.CommandText = @"
                                    UPDATE Highlights
                                    SET RecordKey = $rk, Field = $field, Color = $color, CreatedUtc = $created,
                                        StartOffset = $start, Length = $len, SelectedText = $text, UpdatedUtc = $updated, DeletedUtc = $deleted
                                    WHERE Id = $id";
                                upd.Parameters.AddWithValue("$id", h.Id);
                                upd.Parameters.AddWithValue("$rk", h.RecordKey);
                                upd.Parameters.AddWithValue("$field", h.Field);
                                upd.Parameters.AddWithValue("$color", h.Color.ToString());
                                upd.Parameters.AddWithValue("$created", h.CreatedUtc.ToString("O"));
                                upd.Parameters.AddWithValue("$start", h.StartOffset);
                                upd.Parameters.AddWithValue("$len", h.Length);
                                upd.Parameters.AddWithValue("$text", (object?)h.SelectedText ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$updated", h.UpdatedUtc.ToString("O"));
                                upd.Parameters.AddWithValue("$deleted", (object?)h.DeletedUtc?.ToString("O") ?? DBNull.Value);
                                upd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // 4. Notes: LWW by Id
                if (payload.Notes != null)
                {
                    foreach (var n in payload.Notes)
                    {
                        using var checkCmd = conn.CreateCommand();
                        checkCmd.Transaction = tx;
                        checkCmd.CommandText = "SELECT UpdatedUtc FROM Notes WHERE Id = $id";
                        checkCmd.Parameters.AddWithValue("$id", n.Id);
                        var existingUpdatedObj = checkCmd.ExecuteScalar();

                        if (existingUpdatedObj == null)
                        {
                            using var ins = conn.CreateCommand();
                            ins.Transaction = tx;
                            ins.CommandText = @"
                                INSERT INTO Notes (Id, RecordKey, Title, Content, Field, StartOffset, Length, CreatedUtc, UpdatedUtc, DeletedUtc)
                                VALUES ($id, $rk, $title, $content, $field, $start, $len, $created, $updated, $deleted)";
                            ins.Parameters.AddWithValue("$id", n.Id);
                            ins.Parameters.AddWithValue("$rk", (object?)n.RecordKey ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$title", (object?)n.Title ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$content", n.Content);
                            ins.Parameters.AddWithValue("$field", (object?)n.Field ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$start", n.Field != null ? n.StartOffset : (object)DBNull.Value);
                            ins.Parameters.AddWithValue("$len", n.Field != null ? n.Length : (object)DBNull.Value);
                            ins.Parameters.AddWithValue("$created", n.CreatedUtc.ToString("O"));
                            ins.Parameters.AddWithValue("$updated", n.UpdatedUtc.ToString("O"));
                            ins.Parameters.AddWithValue("$deleted", (object?)n.DeletedUtc?.ToString("O") ?? DBNull.Value);
                            ins.ExecuteNonQuery();
                        }
                        else
                        {
                            var existingUpdated = DateTime.Parse(existingUpdatedObj.ToString()!).ToUniversalTime();
                            if (n.UpdatedUtc >= existingUpdated)
                            {
                                using var upd = conn.CreateCommand();
                                upd.Transaction = tx;
                                upd.CommandText = @"
                                    UPDATE Notes
                                    SET RecordKey = $rk, Title = $title, Content = $content, Field = $field,
                                        StartOffset = $start, Length = $len, CreatedUtc = $created, UpdatedUtc = $updated, DeletedUtc = $deleted
                                    WHERE Id = $id";
                                upd.Parameters.AddWithValue("$id", n.Id);
                                upd.Parameters.AddWithValue("$rk", (object?)n.RecordKey ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$title", (object?)n.Title ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$content", n.Content);
                                upd.Parameters.AddWithValue("$field", (object?)n.Field ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$start", n.Field != null ? n.StartOffset : (object)DBNull.Value);
                                upd.Parameters.AddWithValue("$len", n.Field != null ? n.Length : (object)DBNull.Value);
                                upd.Parameters.AddWithValue("$created", n.CreatedUtc.ToString("O"));
                                upd.Parameters.AddWithValue("$updated", n.UpdatedUtc.ToString("O"));
                                upd.Parameters.AddWithValue("$deleted", (object?)n.DeletedUtc?.ToString("O") ?? DBNull.Value);
                                upd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // 5. ReadingHistory: Upsert with MAX values
                if (payload.ReadingHistory != null)
                {
                    foreach (var rh in payload.ReadingHistory)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO ReadingHistory (RecordKey, LastOpenedUtc, OpenCount)
                            VALUES ($rk, $time, $count)
                            ON CONFLICT(RecordKey) DO UPDATE SET
                                LastOpenedUtc = MAX(ReadingHistory.LastOpenedUtc, excluded.LastOpenedUtc),
                                OpenCount = MAX(ReadingHistory.OpenCount, excluded.OpenCount);";
                        cmd.Parameters.AddWithValue("$rk", rh.RecordKey);
                        cmd.Parameters.AddWithValue("$time", rh.LastOpenedUtc.ToString("O"));
                        cmd.Parameters.AddWithValue("$count", rh.OpenCount);
                        cmd.ExecuteNonQuery();
                    }
                }

                // 6. Settings: LWW
                if (payload.Settings != null)
                {
                    using var checkCmd = conn.CreateCommand();
                    checkCmd.Transaction = tx;
                    checkCmd.CommandText = "SELECT UpdatedUtc FROM UserSettings WHERE Id = 1";
                    var existingUpdatedObj = checkCmd.ExecuteScalar();
                    bool shouldUpdate = true;
                    if (existingUpdatedObj != null && DateTime.TryParse(existingUpdatedObj.ToString(), out var existingSettingsUpdated))
                    {
                        shouldUpdate = payload.Settings.UpdatedUtc >= existingSettingsUpdated;
                    }

                    if (shouldUpdate)
                    {
                        string? paletteJson = payload.Settings.HighlightPalette != null && payload.Settings.HighlightPalette.Count > 0
                            ? JsonSerializer.Serialize(payload.Settings.HighlightPalette)
                            : null;
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            UPDATE UserSettings SET
                                Theme = $theme,
                                FontSize = $fontSize,
                                ReadingWidth = $width,
                                LineSpacing = $spacing,
                                FocusModeEnabled = $focus,
                                ShowTransliteration = $showTranslit,
                                ShowSynonyms = $showSyn,
                                ShowPurport = $showPurport,
                                HighlightPalette = COALESCE($palette, HighlightPalette),
                                UpdatedUtc = $updated
                            WHERE Id = 1";
                        cmd.Parameters.AddWithValue("$theme", payload.Settings.Theme.ToString());
                        cmd.Parameters.AddWithValue("$fontSize", payload.Settings.FontSize.ToString());
                        cmd.Parameters.AddWithValue("$width", payload.Settings.ReadingWidth.ToString());
                        cmd.Parameters.AddWithValue("$spacing", payload.Settings.LineSpacing.ToString());
                        cmd.Parameters.AddWithValue("$focus", payload.Settings.FocusModeEnabled ? 1 : 0);
                        cmd.Parameters.AddWithValue("$showTranslit", payload.Settings.ShowTransliteration ? 1 : 0);
                        cmd.Parameters.AddWithValue("$showSyn", payload.Settings.ShowSynonyms ? 1 : 0);
                        cmd.Parameters.AddWithValue("$showPurport", payload.Settings.ShowPurport ? 1 : 0);
                        cmd.Parameters.AddWithValue("$palette", (object?)paletteJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$updated", payload.Settings.UpdatedUtc.ToString("O"));
                        cmd.ExecuteNonQuery();
                    }
                }

                // 7. Rebuild FTS
                using (var ftsCmd = conn.CreateCommand())
                {
                    ftsCmd.Transaction = tx;
                    ftsCmd.CommandText = @"
                        DELETE FROM NotesFts;
                        INSERT INTO NotesFts(rowid, RecordKey, Content)
                        SELECT rowid, COALESCE(RecordKey, ''), Content FROM Notes WHERE DeletedUtc IS NULL;

                        DELETE FROM HighlightsFts;
                        INSERT INTO HighlightsFts(rowid, RecordKey, SelectedText)
                        SELECT rowid, RecordKey, SelectedText FROM Highlights WHERE SelectedText IS NOT NULL AND DeletedUtc IS NULL;
                    ";
                    ftsCmd.ExecuteNonQuery();
                }

                tx.Commit();
            });
        }

        private static void InsertBookmark(SqliteConnection conn, SqliteTransaction tx, UserBookmark b)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
                INSERT INTO Bookmarks (Id, RecordKey, CollectionId, Title, CreatedUtc, UpdatedUtc, DeletedUtc)
                VALUES ($id, $rk, $cid, $title, $created, $updated, $deleted)";
            ins.Parameters.AddWithValue("$id", b.Id);
            ins.Parameters.AddWithValue("$rk", b.RecordKey);
            ins.Parameters.AddWithValue("$cid", (object?)b.CollectionId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$title", (object?)b.Title ?? DBNull.Value);
            ins.Parameters.AddWithValue("$created", b.CreatedUtc.ToString("O"));
            ins.Parameters.AddWithValue("$updated", b.UpdatedUtc.ToString("O"));
            ins.Parameters.AddWithValue("$deleted", (object?)b.DeletedUtc?.ToString("O") ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }

        public async Task<LocalChangeSet> GetLocalChangesAsync(DateTime? sinceUtc)
        {
            return await Task.Run(() =>
            {
                var changeSet = new LocalChangeSet();
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                string? sinceIso = sinceUtc?.ToUniversalTime().ToString("O");

                // 1. Collections
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sinceIso != null
                        ? "SELECT Id, Name, CreatedUtc, UpdatedUtc, DeletedUtc, SortOrder FROM BookmarkCollections WHERE UpdatedUtc > $since ORDER BY UpdatedUtc ASC"
                        : "SELECT Id, Name, CreatedUtc, UpdatedUtc, DeletedUtc, SortOrder FROM BookmarkCollections ORDER BY UpdatedUtc ASC";
                    if (sinceIso != null) cmd.Parameters.AddWithValue("$since", sinceIso);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        changeSet.Collections.Add(new BookmarkCollection
                        {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            CreatedUtc = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                            UpdatedUtc = reader.IsDBNull(3) ? DateTime.Parse(reader.GetString(2)).ToUniversalTime() : DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
                            DeletedUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)).ToUniversalTime(),
                            SortOrder = reader.GetInt32(5)
                        });
                    }
                }

                // 2. Bookmarks
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sinceIso != null
                        ? "SELECT Id, RecordKey, CreatedUtc, UpdatedUtc, CollectionId, Title, DeletedUtc FROM Bookmarks WHERE UpdatedUtc > $since ORDER BY UpdatedUtc ASC"
                        : "SELECT Id, RecordKey, CreatedUtc, UpdatedUtc, CollectionId, Title, DeletedUtc FROM Bookmarks ORDER BY UpdatedUtc ASC";
                    if (sinceIso != null) cmd.Parameters.AddWithValue("$since", sinceIso);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        changeSet.Bookmarks.Add(new UserBookmark
                        {
                            Id = reader.GetString(0),
                            RecordKey = reader.GetString(1),
                            CreatedUtc = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                            UpdatedUtc = DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
                            CollectionId = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Title = reader.IsDBNull(5) ? null : reader.GetString(5),
                            DeletedUtc = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)).ToUniversalTime()
                        });
                    }
                }

                // 3. Highlights
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sinceIso != null
                        ? $"SELECT {HighlightColumns} FROM Highlights WHERE UpdatedUtc > $since ORDER BY UpdatedUtc ASC"
                        : $"SELECT {HighlightColumns} FROM Highlights ORDER BY UpdatedUtc ASC";
                    if (sinceIso != null) cmd.Parameters.AddWithValue("$since", sinceIso);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) changeSet.Highlights.Add(ReadHighlight(reader));
                }

                // 4. Notes
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sinceIso != null
                        ? $"SELECT {NoteColumns} FROM Notes WHERE UpdatedUtc > $since ORDER BY UpdatedUtc ASC"
                        : $"SELECT {NoteColumns} FROM Notes ORDER BY UpdatedUtc ASC";
                    if (sinceIso != null) cmd.Parameters.AddWithValue("$since", sinceIso);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) changeSet.Notes.Add(ReadNote(reader));
                }

                return changeSet;
            });
        }

        public async Task<int> MergeSyncChangesAsync(RemoteChangeSet remoteChanges, string localDeviceId)
        {
            ArgumentNullException.ThrowIfNull(remoteChanges);

            return await Task.Run(() =>
            {
                int conflicts = 0;
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                string nowIso = DateTime.UtcNow.ToString("O");

                // 1. Collections
                if (remoteChanges.Collections != null)
                {
                    foreach (var c in remoteChanges.Collections)
                    {
                        using var checkCmd = conn.CreateCommand();
                        checkCmd.Transaction = tx;
                        checkCmd.CommandText = "SELECT UpdatedUtc FROM BookmarkCollections WHERE Id = $id";
                        checkCmd.Parameters.AddWithValue("$id", c.Id);
                        var existingUpdatedObj = checkCmd.ExecuteScalar();

                        if (existingUpdatedObj == null)
                        {
                            using var ins = conn.CreateCommand();
                            ins.Transaction = tx;
                            ins.CommandText = @"
                                INSERT INTO BookmarkCollections (Id, Name, CreatedUtc, UpdatedUtc, DeletedUtc, SortOrder)
                                VALUES ($id, $name, $created, $updated, $deleted, $sort)";
                            ins.Parameters.AddWithValue("$id", c.Id);
                            ins.Parameters.AddWithValue("$name", c.Name);
                            ins.Parameters.AddWithValue("$created", c.CreatedUtc.ToUniversalTime().ToString("O"));
                            ins.Parameters.AddWithValue("$updated", c.UpdatedUtc.ToUniversalTime().ToString("O"));
                            ins.Parameters.AddWithValue("$deleted", (object?)c.DeletedUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$sort", c.SortOrder);
                            ins.ExecuteNonQuery();
                        }
                        else
                        {
                            var existingUpdated = DateTime.Parse(existingUpdatedObj.ToString()!).ToUniversalTime();
                            var incomingUpdated = c.UpdatedUtc.ToUniversalTime();

                            bool remoteWins;
                            if (incomingUpdated > existingUpdated)
                            {
                                remoteWins = true;
                            }
                            else if (incomingUpdated < existingUpdated)
                            {
                                remoteWins = false;
                                conflicts++;
                            }
                            else
                            {
                                remoteWins = string.CompareOrdinal(c.Id, localDeviceId) < 0;
                            }

                            if (remoteWins)
                            {
                                using var upd = conn.CreateCommand();
                                upd.Transaction = tx;
                                upd.CommandText = @"
                                    UPDATE BookmarkCollections
                                    SET Name = $name, CreatedUtc = $created, UpdatedUtc = $updated, DeletedUtc = $deleted, SortOrder = $sort
                                    WHERE Id = $id";
                                upd.Parameters.AddWithValue("$id", c.Id);
                                upd.Parameters.AddWithValue("$name", c.Name);
                                upd.Parameters.AddWithValue("$created", c.CreatedUtc.ToUniversalTime().ToString("O"));
                                upd.Parameters.AddWithValue("$updated", incomingUpdated.ToString("O"));
                                upd.Parameters.AddWithValue("$deleted", (object?)c.DeletedUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$sort", c.SortOrder);
                                upd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // 2. Bookmarks: LWW with verse uniqueness guard
                if (remoteChanges.Bookmarks != null)
                {
                    foreach (var b in remoteChanges.Bookmarks)
                    {
                        var incomingUpdated = b.UpdatedUtc.ToUniversalTime();

                        using var checkCmd = conn.CreateCommand();
                        checkCmd.Transaction = tx;
                        checkCmd.CommandText = "SELECT UpdatedUtc FROM Bookmarks WHERE Id = $id";
                        checkCmd.Parameters.AddWithValue("$id", b.Id);
                        var existingUpdatedObj = checkCmd.ExecuteScalar();

                        if (existingUpdatedObj != null)
                        {
                            var existingUpdated = DateTime.Parse(existingUpdatedObj.ToString()!).ToUniversalTime();
                            bool remoteWins;
                            if (incomingUpdated > existingUpdated)
                            {
                                remoteWins = true;
                            }
                            else if (incomingUpdated < existingUpdated)
                            {
                                remoteWins = false;
                                conflicts++;
                            }
                            else
                            {
                                remoteWins = string.CompareOrdinal(b.Id, localDeviceId) < 0;
                            }

                            if (remoteWins)
                            {
                                if (b.DeletedUtc == null)
                                {
                                    // Incoming is active: ensure no other active bookmark exists for this RecordKey
                                    using var tombOther = conn.CreateCommand();
                                    tombOther.Transaction = tx;
                                    tombOther.CommandText = "UPDATE Bookmarks SET DeletedUtc = $now, UpdatedUtc = $now WHERE RecordKey = $rk AND Id != $id AND DeletedUtc IS NULL";
                                    tombOther.Parameters.AddWithValue("$rk", b.RecordKey);
                                    tombOther.Parameters.AddWithValue("$id", b.Id);
                                    tombOther.Parameters.AddWithValue("$now", nowIso);
                                    tombOther.ExecuteNonQuery();
                                }

                                using var upd = conn.CreateCommand();
                                upd.Transaction = tx;
                                upd.CommandText = @"
                                    UPDATE Bookmarks
                                    SET RecordKey = $rk, CollectionId = $cid, Title = $title, CreatedUtc = $created, UpdatedUtc = $updated, DeletedUtc = $deleted
                                    WHERE Id = $id";
                                upd.Parameters.AddWithValue("$id", b.Id);
                                upd.Parameters.AddWithValue("$rk", b.RecordKey);
                                upd.Parameters.AddWithValue("$cid", (object?)b.CollectionId ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$title", (object?)b.Title ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$created", b.CreatedUtc.ToUniversalTime().ToString("O"));
                                upd.Parameters.AddWithValue("$updated", incomingUpdated.ToString("O"));
                                upd.Parameters.AddWithValue("$deleted", (object?)b.DeletedUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value);
                                upd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            // New row by ID
                            if (b.DeletedUtc == null)
                            {
                                // Check if another active bookmark exists for RecordKey
                                using var checkActive = conn.CreateCommand();
                                checkActive.Transaction = tx;
                                checkActive.CommandText = "SELECT Id, UpdatedUtc FROM Bookmarks WHERE RecordKey = $rk AND DeletedUtc IS NULL LIMIT 1";
                                checkActive.Parameters.AddWithValue("$rk", b.RecordKey);
                                using var r = checkActive.ExecuteReader();
                                if (r.Read())
                                {
                                    string existingId = r.GetString(0);
                                    DateTime activeUpdated = DateTime.Parse(r.GetString(1)).ToUniversalTime();
                                    r.Close();

                                    if (incomingUpdated >= activeUpdated)
                                    {
                                        // Incoming wins: tombstone existing active
                                        using var tombOther = conn.CreateCommand();
                                        tombOther.Transaction = tx;
                                        tombOther.CommandText = "UPDATE Bookmarks SET DeletedUtc = $now, UpdatedUtc = $now WHERE Id = $id";
                                        tombOther.Parameters.AddWithValue("$id", existingId);
                                        tombOther.Parameters.AddWithValue("$now", nowIso);
                                        tombOther.ExecuteNonQuery();

                                        InsertBookmark(conn, tx, b);
                                    }
                                    else
                                    {
                                        // Local wins: insert incoming as tombstoned
                                        conflicts++;
                                        var tombstonedCopy = new UserBookmark
                                        {
                                            Id = b.Id,
                                            RecordKey = b.RecordKey,
                                            CollectionId = b.CollectionId,
                                            Title = b.Title,
                                            CreatedUtc = b.CreatedUtc,
                                            UpdatedUtc = b.UpdatedUtc,
                                            DeletedUtc = b.UpdatedUtc
                                        };
                                        InsertBookmark(conn, tx, tombstonedCopy);
                                    }
                                }
                                else
                                {
                                    r.Close();
                                    InsertBookmark(conn, tx, b);
                                }
                            }
                            else
                            {
                                InsertBookmark(conn, tx, b);
                            }
                        }
                    }
                }

                // 3. Highlights: LWW
                if (remoteChanges.Highlights != null)
                {
                    foreach (var h in remoteChanges.Highlights)
                    {
                        var incomingUpdated = h.UpdatedUtc.ToUniversalTime();

                        using var checkCmd = conn.CreateCommand();
                        checkCmd.Transaction = tx;
                        checkCmd.CommandText = "SELECT UpdatedUtc FROM Highlights WHERE Id = $id";
                        checkCmd.Parameters.AddWithValue("$id", h.Id);
                        var existingUpdatedObj = checkCmd.ExecuteScalar();

                        if (existingUpdatedObj == null)
                        {
                            using var ins = conn.CreateCommand();
                            ins.Transaction = tx;
                            ins.CommandText = @"
                                INSERT INTO Highlights (Id, RecordKey, Field, Color, CreatedUtc, StartOffset, Length, SelectedText, UpdatedUtc, DeletedUtc)
                                VALUES ($id, $rk, $field, $color, $created, $start, $len, $text, $updated, $deleted)";
                            ins.Parameters.AddWithValue("$id", h.Id);
                            ins.Parameters.AddWithValue("$rk", h.RecordKey);
                            ins.Parameters.AddWithValue("$field", h.Field);
                            ins.Parameters.AddWithValue("$color", h.Color.ToString());
                            ins.Parameters.AddWithValue("$created", h.CreatedUtc.ToUniversalTime().ToString("O"));
                            ins.Parameters.AddWithValue("$start", h.StartOffset);
                            ins.Parameters.AddWithValue("$len", h.Length);
                            ins.Parameters.AddWithValue("$text", (object?)h.SelectedText ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$updated", incomingUpdated.ToString("O"));
                            ins.Parameters.AddWithValue("$deleted", (object?)h.DeletedUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value);
                            ins.ExecuteNonQuery();
                        }
                        else
                        {
                            var existingUpdated = DateTime.Parse(existingUpdatedObj.ToString()!).ToUniversalTime();
                            bool remoteWins;
                            if (incomingUpdated > existingUpdated)
                            {
                                remoteWins = true;
                            }
                            else if (incomingUpdated < existingUpdated)
                            {
                                remoteWins = false;
                                conflicts++;
                            }
                            else
                            {
                                remoteWins = string.CompareOrdinal(h.Id, localDeviceId) < 0;
                            }

                            if (remoteWins)
                            {
                                using var upd = conn.CreateCommand();
                                upd.Transaction = tx;
                                upd.CommandText = @"
                                    UPDATE Highlights
                                    SET RecordKey = $rk, Field = $field, Color = $color, CreatedUtc = $created,
                                        StartOffset = $start, Length = $len, SelectedText = $text, UpdatedUtc = $updated, DeletedUtc = $deleted
                                    WHERE Id = $id";
                                upd.Parameters.AddWithValue("$id", h.Id);
                                upd.Parameters.AddWithValue("$rk", h.RecordKey);
                                upd.Parameters.AddWithValue("$field", h.Field);
                                upd.Parameters.AddWithValue("$color", h.Color.ToString());
                                upd.Parameters.AddWithValue("$created", h.CreatedUtc.ToUniversalTime().ToString("O"));
                                upd.Parameters.AddWithValue("$start", h.StartOffset);
                                upd.Parameters.AddWithValue("$len", h.Length);
                                upd.Parameters.AddWithValue("$text", (object?)h.SelectedText ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$updated", incomingUpdated.ToString("O"));
                                upd.Parameters.AddWithValue("$deleted", (object?)h.DeletedUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value);
                                upd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // 4. Notes: LWW
                if (remoteChanges.Notes != null)
                {
                    foreach (var n in remoteChanges.Notes)
                    {
                        var incomingUpdated = n.UpdatedUtc.ToUniversalTime();

                        using var checkCmd = conn.CreateCommand();
                        checkCmd.Transaction = tx;
                        checkCmd.CommandText = "SELECT UpdatedUtc FROM Notes WHERE Id = $id";
                        checkCmd.Parameters.AddWithValue("$id", n.Id);
                        var existingUpdatedObj = checkCmd.ExecuteScalar();

                        if (existingUpdatedObj == null)
                        {
                            using var ins = conn.CreateCommand();
                            ins.Transaction = tx;
                            ins.CommandText = @"
                                INSERT INTO Notes (Id, RecordKey, Title, Content, Field, StartOffset, Length, CreatedUtc, UpdatedUtc, DeletedUtc)
                                VALUES ($id, $rk, $title, $content, $field, $start, $len, $created, $updated, $deleted)";
                            ins.Parameters.AddWithValue("$id", n.Id);
                            ins.Parameters.AddWithValue("$rk", (object?)n.RecordKey ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$title", (object?)n.Title ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$content", n.Content);
                            ins.Parameters.AddWithValue("$field", (object?)n.Field ?? DBNull.Value);
                            ins.Parameters.AddWithValue("$start", n.Field != null ? n.StartOffset : (object)DBNull.Value);
                            ins.Parameters.AddWithValue("$len", n.Field != null ? n.Length : (object)DBNull.Value);
                            ins.Parameters.AddWithValue("$created", n.CreatedUtc.ToUniversalTime().ToString("O"));
                            ins.Parameters.AddWithValue("$updated", incomingUpdated.ToString("O"));
                            ins.Parameters.AddWithValue("$deleted", (object?)n.DeletedUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value);
                            ins.ExecuteNonQuery();
                        }
                        else
                        {
                            var existingUpdated = DateTime.Parse(existingUpdatedObj.ToString()!).ToUniversalTime();
                            bool remoteWins;
                            if (incomingUpdated > existingUpdated)
                            {
                                remoteWins = true;
                            }
                            else if (incomingUpdated < existingUpdated)
                            {
                                remoteWins = false;
                                conflicts++;
                            }
                            else
                            {
                                remoteWins = string.CompareOrdinal(n.Id, localDeviceId) < 0;
                            }

                            if (remoteWins)
                            {
                                using var upd = conn.CreateCommand();
                                upd.Transaction = tx;
                                upd.CommandText = @"
                                    UPDATE Notes
                                    SET RecordKey = $rk, Title = $title, Content = $content, Field = $field,
                                        StartOffset = $start, Length = $len, CreatedUtc = $created, UpdatedUtc = $updated, DeletedUtc = $deleted
                                    WHERE Id = $id";
                                upd.Parameters.AddWithValue("$id", n.Id);
                                upd.Parameters.AddWithValue("$rk", (object?)n.RecordKey ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$title", (object?)n.Title ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$content", n.Content);
                                upd.Parameters.AddWithValue("$field", (object?)n.Field ?? DBNull.Value);
                                upd.Parameters.AddWithValue("$start", n.Field != null ? n.StartOffset : (object)DBNull.Value);
                                upd.Parameters.AddWithValue("$len", n.Field != null ? n.Length : (object)DBNull.Value);
                                upd.Parameters.AddWithValue("$created", n.CreatedUtc.ToUniversalTime().ToString("O"));
                                upd.Parameters.AddWithValue("$updated", incomingUpdated.ToString("O"));
                                upd.Parameters.AddWithValue("$deleted", (object?)n.DeletedUtc?.ToUniversalTime().ToString("O") ?? DBNull.Value);
                                upd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // 5. Rebuild FTS
                using (var ftsCmd = conn.CreateCommand())
                {
                    ftsCmd.Transaction = tx;
                    ftsCmd.CommandText = @"
                        DELETE FROM NotesFts;
                        INSERT INTO NotesFts(rowid, RecordKey, Content)
                        SELECT rowid, COALESCE(RecordKey, ''), Content FROM Notes WHERE DeletedUtc IS NULL;

                        DELETE FROM HighlightsFts;
                        INSERT INTO HighlightsFts(rowid, RecordKey, SelectedText)
                        SELECT rowid, RecordKey, SelectedText FROM Highlights WHERE SelectedText IS NOT NULL AND DeletedUtc IS NULL;
                    ";
                    ftsCmd.ExecuteNonQuery();
                }

                tx.Commit();
                return conflicts;
            });
        }

        public async Task<Dictionary<string, string>> GetBookCategoryOverridesAsync()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT BookKey, Category FROM BookCategoryOverrides;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                dict[reader.GetString(0)] = reader.GetString(1);
            }
            return dict;
        }

        public async Task SetBookCategoryOverrideAsync(string bookKey, string category)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BookCategoryOverrides (BookKey, Category, UpdatedUtc)
                VALUES (@BookKey, @Category, @UpdatedUtc)
                ON CONFLICT(BookKey) DO UPDATE SET Category = excluded.Category, UpdatedUtc = excluded.UpdatedUtc;";
            cmd.Parameters.AddWithValue("@BookKey", bookKey.Trim());
            cmd.Parameters.AddWithValue("@Category", category.Trim());
            cmd.Parameters.AddWithValue("@UpdatedUtc", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveBookCategoryOverrideAsync(string bookKey)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM BookCategoryOverrides WHERE BookKey = @BookKey;";
            cmd.Parameters.AddWithValue("@BookKey", bookKey.Trim());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<string>> GetCustomFoldersAsync()
        {
            var list = new List<string>();
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM CustomFolders ORDER BY SortOrder, Name;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(reader.GetString(0));
            }
            return list;
        }

        public async Task AddCustomFolderAsync(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return;
            string cleanName = folderName.Trim();
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO CustomFolders (Id, Name, SortOrder, CreatedUtc)
                VALUES (@Id, @Name, 0, @CreatedUtc);";
            cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@Name", cleanName);
            cmd.Parameters.AddWithValue("@CreatedUtc", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteCustomFolderAsync(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return;
            string cleanName = folderName.Trim();
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CustomFolders WHERE Name = @Name;";
            cmd.Parameters.AddWithValue("@Name", cleanName);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<Dictionary<string, int>> GetBookDisplayOrderAsync()
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT BookKey, SortOrder FROM BookDisplayOrder;";
            try
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    dict[reader.GetString(0)] = reader.GetInt32(1);
                }
            }
            catch { }
            return dict;
        }

        public async Task SetBookDisplayOrderAsync(string bookKey, int order)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BookDisplayOrder (BookKey, SortOrder, UpdatedUtc)
                VALUES (@BookKey, @SortOrder, @UpdatedUtc)
                ON CONFLICT(BookKey) DO UPDATE SET SortOrder = excluded.SortOrder, UpdatedUtc = excluded.UpdatedUtc;";
            cmd.Parameters.AddWithValue("@BookKey", bookKey.Trim());
            cmd.Parameters.AddWithValue("@SortOrder", order);
            cmd.Parameters.AddWithValue("@UpdatedUtc", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetBooksDisplayOrderAsync(Dictionary<string, int> bookOrders)
        {
            if (bookOrders == null || bookOrders.Count == 0) return;
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO BookDisplayOrder (BookKey, SortOrder, UpdatedUtc)
                VALUES (@BookKey, @SortOrder, @UpdatedUtc)
                ON CONFLICT(BookKey) DO UPDATE SET SortOrder = excluded.SortOrder, UpdatedUtc = excluded.UpdatedUtc;";
            var pKey = cmd.Parameters.Add("@BookKey", SqliteType.Text);
            var pOrder = cmd.Parameters.Add("@SortOrder", SqliteType.Integer);
            var pUpdated = cmd.Parameters.Add("@UpdatedUtc", SqliteType.Text);
            string now = DateTime.UtcNow.ToString("O");
            pUpdated.Value = now;

            foreach (var kvp in bookOrders)
            {
                pKey.Value = kvp.Key.Trim();
                pOrder.Value = kvp.Value;
                await cmd.ExecuteNonQueryAsync();
            }
            tx.Commit();
        }

        public async Task ResetBookDisplayOrderForKeysAsync(IEnumerable<string> bookKeys)
        {
            if (bookKeys == null) return;
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM BookDisplayOrder WHERE BookKey = @BookKey;";
            var pKey = cmd.Parameters.Add("@BookKey", SqliteType.Text);

            foreach (var key in bookKeys)
            {
                pKey.Value = key.Trim();
                await cmd.ExecuteNonQueryAsync();
            }
            tx.Commit();
        }

        public async Task<List<string>> GetFolderDisplayOrderAsync()
        {
            var raw = await GetSyncMetadataAsync("FolderDisplayOrder");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task SetFolderDisplayOrderAsync(List<string> orderedFolders)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(orderedFolders ?? new List<string>());
            await SetSyncMetadataAsync("FolderDisplayOrder", json);
        }
    }
}
