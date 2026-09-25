using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VedaBaseModern.Core.Models
{
    /// <summary>
    /// Set of local entities modified since a specified sync checkpoint (Phase 5).
    /// Includes both active and tombstoned (DeletedUtc != null) rows.
    /// </summary>
    public class LocalChangeSet
    {
        public List<BookmarkCollection> Collections { get; set; } = new();
        public List<UserBookmark> Bookmarks { get; set; } = new();
        public List<Highlight> Highlights { get; set; } = new();
        public List<UserNote> Notes { get; set; } = new();

        public int TotalCount => Collections.Count + Bookmarks.Count + Highlights.Count + Notes.Count;
    }

    /// <summary>
    /// Set of remote entities fetched from the cloud provider (Phase 5).
    /// </summary>
    public class RemoteChangeSet
    {
        public List<BookmarkCollection> Collections { get; set; } = new();
        public List<UserBookmark> Bookmarks { get; set; } = new();
        public List<Highlight> Highlights { get; set; } = new();
        public List<UserNote> Notes { get; set; } = new();

        public int TotalCount => Collections.Count + Bookmarks.Count + Highlights.Count + Notes.Count;
    }

    /// <summary>
    /// <summary>
    /// Periodic sync interval options (Phase 6).
    /// </summary>
    public enum SyncIntervalOption
    {
        Disabled = 0,
        Every15Minutes = 15,
        Every30Minutes = 30,
        EveryHour = 60,
        Every6Hours = 360,
        Daily = 1440
    }

    /// <summary>
    /// Formal, explicit synchronization states (Phase 7). Each value is a
    /// distinct, mutually-exclusive condition the app can be in with respect
    /// to cloud sync - deriving the UI's "Connected / Offline / Syncing..."
    /// text from one authoritative source instead of ad-hoc boolean checks
    /// scattered across ViewModels.
    /// </summary>
    public enum SyncState
    {
        /// <summary>No provider has ever been configured. 100% offline by choice.</summary>
        NotConfigured,
        /// <summary>A provider is configured but SyncNowAsync has never completed successfully.</summary>
        ConfiguredNeverSynced,
        /// <summary>A sync cycle is actively running right now.</summary>
        Syncing,
        /// <summary>Last sync succeeded and no local changes have been made since.</summary>
        Synced,
        /// <summary>Last sync succeeded, but local records have changed since (will be picked up by the next sync).</summary>
        LocalChangesPending,
        /// <summary>The device appears to have no network connectivity.</summary>
        Offline,
        /// <summary>The last sync attempt failed authentication (bad email/password or expired session that could not be refreshed).</summary>
        AuthenticationFailure,
        /// <summary>The last sync attempt was rejected by Supabase Row Level Security or another permission boundary.</summary>
        PermissionFailure,
        /// <summary>The last sync attempt failed for a network-level reason (timeout, DNS, connection reset, 5xx).</summary>
        NetworkFailure,
        /// <summary>The last sync attempt failed for a reason not covered by the categories above.</summary>
        SyncFailed
    }

    /// <summary>
    /// Coarse categorization of why a sync attempt failed - drives both
    /// SyncState selection and the "Last error category" diagnostics field,
    /// without ever needing to inspect exception messages by string-matching
    /// in the UI layer.
    /// </summary>
    public enum SyncErrorCategory
    {
        None,
        Authentication,
        Permission,
        Network,
        RateLimited,
        ServerError,
        Canceled,
        Other
    }

    /// <summary>
    /// Live sync status information for UI display.
    /// </summary>
    public class SyncStatusInfo
    {
        public bool IsConfigured { get; set; }
        public string ProviderType { get; set; } = "None";
        public DateTime? LastSyncUtc { get; set; }
        public string LastSyncResult { get; set; } = string.Empty;
        public string LastSyncMessage { get; set; } = string.Empty;
        public int UploadedCount { get; set; }
        public int DownloadedCount { get; set; }
        public int ConflictCount { get; set; }
        public bool IsSyncing { get; set; }
        public string? ProjectUrl { get; set; }
        public string? UserEmail { get; set; }
        public int SyncIntervalMinutes { get; set; }
        public bool IsPeriodicSyncEnabled => SyncIntervalMinutes > 0;

        /// <summary>Local records changed (UpdatedUtc) since LastSyncUtc - "pending" count shown in Settings.</summary>
        public int PendingLocalChanges { get; set; }

        public SyncErrorCategory LastErrorCategory { get; set; } = SyncErrorCategory.None;

        /// <summary>
        /// Derives the single authoritative SyncState from the other fields
        /// on this object - see SyncState's own doc comments for what each
        /// value means. Pure computation, no I/O, safe to call repeatedly
        /// from XAML bindings.
        /// </summary>
        public SyncState State
        {
            get
            {
                if (IsSyncing) return SyncState.Syncing;
                if (!IsConfigured) return SyncState.NotConfigured;
                if (!LastSyncUtc.HasValue) return SyncState.ConfiguredNeverSynced;

                if (!string.Equals(LastSyncResult, "Success", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(LastSyncResult, "Disconnected", StringComparison.OrdinalIgnoreCase))
                {
                    return LastErrorCategory switch
                    {
                        SyncErrorCategory.Authentication => SyncState.AuthenticationFailure,
                        SyncErrorCategory.Permission => SyncState.PermissionFailure,
                        SyncErrorCategory.Network or SyncErrorCategory.RateLimited or SyncErrorCategory.ServerError => SyncState.NetworkFailure,
                        _ => SyncState.SyncFailed
                    };
                }

                return PendingLocalChanges > 0 ? SyncState.LocalChangesPending : SyncState.Synced;
            }
        }
    }

    /// <summary>
    /// Lightweight, redaction-safe sync diagnostics snapshot (Phase 7). Every
    /// field here is sourced from SyncMetadata (plain, non-secret values) -
    /// never from ICredentialStorageService, so there is no secret value
    /// that could accidentally leak into this model or its exported JSON.
    /// </summary>
    public class SyncDiagnostics
    {
        public SyncState State { get; set; }
        public string ProviderType { get; set; } = "None";
        public DateTime? LastSuccessfulSyncUtc { get; set; }
        public DateTime? LastAttemptedSyncUtc { get; set; }
        public double? LastSyncDurationSeconds { get; set; }
        public int RecordsUploaded { get; set; }
        public int RecordsDownloaded { get; set; }
        public int ConflictsResolved { get; set; }
        public int PendingLocalChanges { get; set; }
        public SyncErrorCategory LastErrorCategory { get; set; } = SyncErrorCategory.None;
        public string? LastErrorMessage { get; set; }
        public string? ProjectUrl { get; set; }
        public string? UserEmail { get; set; }
        public int SyncIntervalMinutes { get; set; }
    }

    /// <summary>
    /// Result of executing a synchronization cycle.
    /// </summary>
    public class SyncResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int UploadedCount { get; set; }
        public int DownloadedCount { get; set; }
        public int ConflictCount { get; set; }
        public string? SnapshotPath { get; set; }
    }

    /// <summary>
    /// Metadata returned from a remote sync provider probe.
    /// </summary>
    public class SyncRemoteMetadata
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTime ServerTimeUtc { get; set; } = DateTime.UtcNow;
        public bool IsCompatible { get; set; } = true;
        public Dictionary<string, int> EntityCounts { get; set; } = new();
    }

    /// <summary>
    /// Result of testing credentials and database connectivity to a provider.
    /// </summary>
    public class SyncConnectionTestResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public bool TablesExist { get; set; }
        public List<string> MissingTables { get; set; } = new();
    }

    /// <summary>
    /// Summary of changes pending synchronization before execution.
    /// </summary>
    public class SyncPreviewResult
    {
        public int LocalChangesCount { get; set; }
        public int RemoteChangesCount { get; set; }
        public string? ErrorMessage { get; set; }
        public bool CanSync => string.IsNullOrEmpty(ErrorMessage);
    }

    /// <summary>
    /// Configuration for a user-owned Supabase instance.
    /// Supports both public/anon mode and authenticated user session mode for RLS.
    /// </summary>
    public class SupabaseConfig
    {
        public string ProjectUrl { get; set; } = string.Empty;
        public string AnonKey { get; set; } = string.Empty;
        public string? UserEmail { get; set; }
        public string? UserPassword { get; set; }
        public string? AuthToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? UserId { get; set; }
    }

    /// <summary>
    /// DTO for Supabase Auth token response.
    /// </summary>
    public class SupabaseAuthResponseDto
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("user")]
        public SupabaseUserInfoDto? User { get; set; }
    }

    public class SupabaseUserInfoDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    // =========================================================================
    // Supabase PostgREST DTOs (snake_case mapping to remote tables)
    // =========================================================================

    public class SupabaseCollectionDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UserId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;
    }

    public class SupabaseBookmarkDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UserId { get; set; }

        [JsonPropertyName("record_key")]
        public string RecordKey { get; set; } = string.Empty;

        [JsonPropertyName("collection_id")]
        public string? CollectionId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;
    }

    public class SupabaseHighlightDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UserId { get; set; }

        [JsonPropertyName("record_key")]
        public string RecordKey { get; set; } = string.Empty;

        [JsonPropertyName("field")]
        public string Field { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public string Color { get; set; } = string.Empty;

        [JsonPropertyName("start_offset")]
        public int StartOffset { get; set; } = -1;

        [JsonPropertyName("length")]
        public int Length { get; set; } = -1;

        [JsonPropertyName("selected_text")]
        public string? SelectedText { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;
    }

    public class SupabaseNoteDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UserId { get; set; }

        [JsonPropertyName("record_key")]
        public string? RecordKey { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("field")]
        public string? Field { get; set; }

        [JsonPropertyName("start_offset")]
        public int? StartOffset { get; set; }

        [JsonPropertyName("length")]
        public int? Length { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("deleted_at")]
        public DateTime? DeletedAt { get; set; }

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;
    }

    public class SupabaseSchemaInfoDto
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
