using System;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Provider-independent synchronization contract (Phase 5).
    /// Decouples the local research database and sync orchestrator from any specific
    /// cloud provider (Supabase, future WebDAV, local P2P, Android companion, etc.).
    /// </summary>
    public interface ISyncProvider
    {
        /// <summary>
        /// Unique provider identifier (e.g. "Supabase", "Mock").
        /// </summary>
        string ProviderId { get; }

        /// <summary>
        /// Tests credentials and checks reachability of the remote database and required tables.
        /// </summary>
        Task<SyncConnectionTestResult> TestConnectionAsync();

        /// <summary>
        /// Fetches remote schema version and server time.
        /// </summary>
        Task<SyncRemoteMetadata> GetRemoteMetadataAsync();

        /// <summary>
        /// Downloads all remote changes (including tombstones) modified since the given timestamp.
        /// </summary>
        Task<RemoteChangeSet> PullChangesAsync(DateTime? sinceUtc);

        /// <summary>
        /// Uploads local changes (including tombstones) to the remote store.
        /// Returns the count of successfully pushed items.
        /// </summary>
        Task<int> PushChangesAsync(LocalChangeSet changes);

        /// <summary>
        /// Disconnects and releases any provider session resources.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Re-authenticates using the stored credentials (Phase 7), producing a
        /// fresh session token. Used to recover from a 401 mid-session without
        /// requiring the user to re-enter credentials or restart the app -
        /// see ResearchSyncService's single-retry-on-401 recovery.
        /// </summary>
        Task<bool> AuthenticateAsync();
    }
}
