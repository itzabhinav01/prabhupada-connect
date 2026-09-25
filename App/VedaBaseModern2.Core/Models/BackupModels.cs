using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VedaBaseModern.Core.Models
{
    /// <summary>
    /// Archive metadata manifest for personal research data backups (Phase 4).
    /// Stored as manifest.json inside the .vdbbackup ZIP container.
    /// </summary>
    public class BackupManifest
    {
        [JsonPropertyName("format_version")]
        public int FormatVersion { get; set; } = 1;

        [JsonPropertyName("app_version")]
        public string AppVersion { get; set; } = "1.0.0";

        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; } = 10;

        [JsonPropertyName("exported_utc")]
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("counts")]
        public BackupCounts Counts { get; set; } = new();

        [JsonPropertyName("data_sha256")]
        public string DataSha256 { get; set; } = string.Empty;

        [JsonPropertyName("corpus_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CorpusId { get; set; }

        [JsonPropertyName("corpus_version")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CorpusVersion { get; set; }

        [JsonPropertyName("corpus_sha256")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CorpusSha256 { get; set; }
    }

    /// <summary>
    /// Entity count summary recorded in BackupManifest.
    /// </summary>
    public class BackupCounts
    {
        [JsonPropertyName("bookmarks")]
        public int Bookmarks { get; set; }

        [JsonPropertyName("collections")]
        public int Collections { get; set; }

        [JsonPropertyName("highlights")]
        public int Highlights { get; set; }

        [JsonPropertyName("notes")]
        public int Notes { get; set; }

        [JsonPropertyName("reading_history")]
        public int ReadingHistory { get; set; }
    }

    /// <summary>
    /// Self-contained payload of all personal research entities (Phase 4).
    /// Serialized into data.json within the .vdbbackup archive.
    /// </summary>
    public class BackupDataPayload
    {
        [JsonPropertyName("bookmarks")]
        public List<UserBookmark> Bookmarks { get; set; } = new();

        [JsonPropertyName("collections")]
        public List<BookmarkCollection> Collections { get; set; } = new();

        [JsonPropertyName("highlights")]
        public List<Highlight> Highlights { get; set; } = new();

        [JsonPropertyName("notes")]
        public List<UserNote> Notes { get; set; } = new();

        [JsonPropertyName("settings")]
        public AppSettings? Settings { get; set; }

        [JsonPropertyName("reading_history")]
        public List<ReadingHistoryEntry> ReadingHistory { get; set; } = new();
    }

    /// <summary>
    /// Import strategy when restoring a personal research backup.
    /// </summary>
    public enum BackupImportMode
    {
        /// <summary>
        /// Replace (Clean Restore): erases existing personal research tables
        /// and completely recreates them from the backup payload.
        /// </summary>
        Replace,

        /// <summary>
        /// Merge: merges incoming entities with local database using field-level
        /// Last-Write-Wins (LWW) based on UpdatedUtc timestamps, preserving tombstones.
        /// </summary>
        Merge
    }

    /// <summary>
    /// Result of inspecting and validating a backup archive file or stream.
    /// </summary>
    public class BackupValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public BackupManifest? Manifest { get; set; }
        public BackupDataPayload? Payload { get; set; }
        public bool IsCorpusCompatible { get; set; } = true;
        public string? CorpusCompatibilityWarning { get; set; }
    }

    /// <summary>
    /// Result of executing a backup restore operation.
    /// </summary>
    public class BackupRestoreResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SnapshotPath { get; set; }
        public BackupCounts? ImportedCounts { get; set; }
    }
}
