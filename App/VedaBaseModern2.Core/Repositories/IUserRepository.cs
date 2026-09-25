using System.Collections.Generic;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Repositories
{
    public interface IUserRepository
    {
        Task InitializeAsync();
        
        // Bookmarks. Since Phase 1 (Research Platform), each bookmark has a
        // stable Id independent of RecordKey; RemoveBookmarkAsync tombstones
        // (DeletedUtc) rather than hard-deleting, and AddBookmarkAsync
        // undeletes a matching tombstoned row instead of inserting a
        // duplicate when one exists - "at most one ACTIVE bookmark per
        // verse" is preserved exactly as before.
        // Database file path on disk (for pre-restore backups/snapshots)
        string DatabasePath { get; }

        Task<bool> IsBookmarkedAsync(string recordKey);
        Task AddBookmarkAsync(string recordKey);
        Task RemoveBookmarkAsync(string recordKey);
        Task<List<UserBookmark>> GetAllBookmarksAsync(bool includeDeleted = false);
        Task<List<UserBookmark>> GetAllBookmarksIncludingDeletedAsync();

        // Bookmark Collections (Phase 4.6). CollectionId null = uncategorized.
        // Since Phase 3 (Migration 10), collections support soft-deletion (tombstoning).
        Task<List<BookmarkCollection>> GetCollectionsAsync(bool includeDeleted = false);
        Task<List<BookmarkCollection>> GetAllCollectionsIncludingDeletedAsync();
        Task<BookmarkCollection> CreateCollectionAsync(string name);
        Task RenameCollectionAsync(string collectionId, string newName);
        // Soft-deletes the collection (sets DeletedUtc/UpdatedUtc) - bookmarks in it become uncategorized
        // (CollectionId set back to NULL, with UpdatedUtc bumped), never deleted.
        Task DeleteCollectionAsync(string collectionId);
        // collectionId may be null to move a bookmark back to "uncategorized".
        Task SetBookmarkCollectionAsync(string recordKey, string? collectionId);

        // Highlights (Phase 4.7 - precise character-offset ranges). A verse
        // may hold many highlights, including several within the same Field,
        // as long as their ranges don't overlap - see
        // ReadingViewModel.RangesOverlap for the overlap-rejection policy.
        Task<List<Highlight>> GetHighlightsAsync(string recordKey);
        // Every highlight across every verse, most recent first - powers the
        // Personal Workspace "Highlights" destination.
        Task<List<Highlight>> GetAllHighlightsAsync();
        Task<List<Highlight>> GetAllHighlightsIncludingDeletedAsync();
        // Creates a NEW highlight (never an upsert - multiple highlights per
        // field are allowed). Returns the created row with its generated Id.
        Task<Highlight> AddHighlightAsync(string recordKey, string field, int startOffset, int length, string selectedText, HighlightColor color);
        // Tombstones exactly one highlight by its own Id (sets DeletedUtc,
        // never a hard DELETE since Phase 1: Research Platform) - not by
        // (RecordKey, Field), since a field may now hold several highlights.
        Task RemoveHighlightAsync(string highlightId);
        Task UpdateHighlightColorAsync(string highlightId, HighlightColor color);

        // Lightweight counts for the Personal Workspace quick-menu badges -
        // COUNT(*) queries, never load full row sets just to size a badge.
        Task<int> GetBookmarkCountAsync();
        Task<int> GetNoteCountAsync();
        Task<int> GetHighlightCountAsync();
        
        // Notes (Phase 2: Research Platform). title/field/startOffset/length
        // are all optional - a note can be a general remark on a verse
        // (no anchor) or anchored to an exact selected range, same
        // (RecordKey, Field, StartOffset, Length) shape as Highlight.
        Task<List<UserNote>> GetNotesAsync(string recordKey);
        // Every note across every verse, most recently updated first - powers
        // the Personal Workspace "Notes" destination.
        Task<List<UserNote>> GetAllNotesAsync();
        Task<List<UserNote>> GetAllNotesIncludingDeletedAsync();
        // recordKey may be null/empty for a general research note with no
        // scripture association.
        Task<UserNote> CreateNoteAsync(string? recordKey, string content, string? title = null, string? field = null, int startOffset = -1, int length = -1);
        // Updates title/content only - the scripture anchor (Field/StartOffset/Length)
        // is set once at creation and never re-anchored by an edit.
        Task UpdateNoteAsync(string id, string content, string? title = null);
        // Soft-delete (tombstone) - see RemoveHighlightAsync for the same pattern.
        Task DeleteNoteAsync(string id);

        // Reading History (Milestone 5)
        // Records (or re-records) that recordKey was actually opened in Reading View,
        // "just now". Atomic upsert - never produces duplicate rows for the same
        // RecordKey, and internally enforces the retention cap. See
        // MILESTONE_5_HISTORY_ARCHITECTURE.md for the exact SQL and rationale.
        Task RecordHistoryAsync(string recordKey);

        // Most-recently-opened records, newest first. Bounded by 'limit'.
        Task<List<ReadingHistoryEntry>> GetRecentHistoryAsync(int limit = 100);
        Task<List<ReadingHistoryEntry>> GetAllHistoryAsync();

        // Deletes ALL reading history. Never touches Bookmarks or Notes.
        Task ClearHistoryAsync();

        // Search user content (Notes and Bookmarks)
        Task<List<UserSearchResult>> SearchUserContentAsync(string query);

        // Sync Metadata & Local Device Identity (Phase 3: Migration 10)
        Task<string?> GetSyncMetadataAsync(string key);
        Task SetSyncMetadataAsync(string key, string value);
        Task<string> GetDeviceIdAsync();

        // Personal Research Backup & Restore (Phase 4)
        Task RestoreDataAsync(BackupDataPayload payload);
        Task MergeDataAsync(BackupDataPayload payload);

        // Offline-First Cloud Sync (Phase 5)
        Task<LocalChangeSet> GetLocalChangesAsync(DateTime? sinceUtc);
        Task<int> MergeSyncChangesAsync(RemoteChangeSet remoteChanges, string localDeviceId);

        // Custom Book Categories / Folders Organization & Reordering
        Task<Dictionary<string, string>> GetBookCategoryOverridesAsync();
        Task SetBookCategoryOverrideAsync(string bookKey, string category);
        Task RemoveBookCategoryOverrideAsync(string bookKey);
        Task<List<string>> GetCustomFoldersAsync();
        Task AddCustomFolderAsync(string folderName);
        Task DeleteCustomFolderAsync(string folderName);
        Task<Dictionary<string, int>> GetBookDisplayOrderAsync();
        Task SetBookDisplayOrderAsync(string bookKey, int order);
        Task SetBooksDisplayOrderAsync(Dictionary<string, int> bookOrders);
        Task ResetBookDisplayOrderForKeysAsync(IEnumerable<string> bookKeys);
        Task<List<string>> GetFolderDisplayOrderAsync();
        Task SetFolderDisplayOrderAsync(List<string> orderedFolders);
    }
}
