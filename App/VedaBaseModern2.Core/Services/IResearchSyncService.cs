using System.Threading;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// High-level orchestration engine for offline-first personal research synchronization (Phase 5).
    /// </summary>
    public interface IResearchSyncService
    {
        ISyncProvider? CurrentProvider { get; }

        /// <summary>
        /// Retrieves the live status, provider details, and timestamps of synchronization.
        /// </summary>
        Task<SyncStatusInfo> GetSyncStatusAsync();

        /// <summary>
        /// Executes a complete manual synchronization cycle:
        /// 1. Captures pre-sync local safety snapshot.
        /// 2. Pulls remote changes since high-water mark.
        /// 3. Merges remote changes into local SQLite via LWW conflict resolution.
        /// 4. Pushes pending local changes to remote.
        /// 5. Commits new high-water mark and stats to SyncMetadata.
        /// </summary>
        Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Inspects pending local and remote changes without modifying data.
        /// </summary>
        Task<SyncPreviewResult> PreviewSyncAsync();

        /// <summary>
        /// Tests credentials, persists them securely via DPAPI, and connects a user-owned Supabase instance.
        /// Supports optional user email and password for account-scoped Row Level Security (RLS).
        /// </summary>
        Task<SyncConnectionTestResult> ConfigureSupabaseAsync(string projectUrl, string apiKey, string? userEmail = null, string? userPassword = null);

        /// <summary>
        /// Configures the periodic background synchronization interval in minutes (0 = disabled/manual only).
        /// </summary>
        Task SetSyncIntervalAsync(int intervalMinutes);

        /// <summary>
        /// Starts periodic synchronization if configured with an interval > 0.
        /// </summary>
        void StartPeriodicSync();

        /// <summary>
        /// Stops any active periodic synchronization timer.
        /// </summary>
        void StopPeriodicSync();

        /// <summary>
        /// Disconnects the active provider and clears credentials while leaving all local research data 100% intact.
        /// </summary>
        Task DisconnectProviderAsync();

        /// <summary>
        /// Injects a custom or mock provider (useful for testing or future alternative backends).
        /// </summary>
        void SetCustomProvider(ISyncProvider provider);

        /// <summary>
        /// Returns a redaction-safe diagnostics snapshot (Phase 7) - every
        /// field is sourced from non-secret SyncMetadata; never touches
        /// ICredentialStorageService, so no secret value can appear here.
        /// </summary>
        Task<SyncDiagnostics> GetDiagnosticsAsync();

        /// <summary>
        /// Writes the current diagnostics snapshot to a JSON file at the given
        /// path for support/troubleshooting purposes. Contains zero secrets.
        /// </summary>
        Task ExportDiagnosticsAsync(string filePath);

        /// <summary>
        /// Reports whether connecting to the given project/email would target
        /// a DIFFERENT project/account than the one currently configured
        /// (Phase 7). Callers should confirm with the user before proceeding
        /// when this returns true, since all current local research data will
        /// be treated as new data for the new project on the next sync - see
        /// RESEARCH_PLATFORM_PHASE7.md "Project Switch Safety".
        /// </summary>
        Task<bool> WouldSwitchProjectAsync(string projectUrl, string? userEmail);
    }
}
