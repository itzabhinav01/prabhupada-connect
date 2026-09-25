using System.IO;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Service for creating, validating, and restoring portable personal research
    /// data archives (.vdbbackup / JSON) independent of cloud connectivity (Phase 4).
    /// </summary>
    public interface IResearchDataBackupService
    {
        /// <summary>
        /// Reads all personal research entities (active and tombstoned) from the local repository.
        /// </summary>
        Task<BackupDataPayload> CreateBackupPayloadAsync();

        /// <summary>
        /// Packages personal research data into a portable .vdbbackup archive (ZIP bytes
        /// containing manifest.json with SHA-256 and data.json).
        /// </summary>
        Task<byte[]> ExportBackupArchiveAsync();

        /// <summary>
        /// Exports the backup archive directly to a file path.
        /// </summary>
        Task ExportBackupToFileAsync(string filePath);

        /// <summary>
        /// Validates archive integrity, manifest version, schema compatibility, and payload checksum
        /// without mutating the local database.
        /// </summary>
        Task<BackupValidationResult> ValidateBackupFileAsync(string filePath);

        /// <summary>
        /// Validates archive stream integrity without mutating the local database.
        /// </summary>
        Task<BackupValidationResult> ValidateBackupStreamAsync(Stream stream);

        /// <summary>
        /// Creates a pre-restore database snapshot and executes either Replace or Merge restore.
        /// </summary>
        Task<BackupRestoreResult> RestoreBackupAsync(string filePath, BackupImportMode mode);

        /// <summary>
        /// Restores from an in-memory or custom stream with pre-restore snapshot.
        /// </summary>
        Task<BackupRestoreResult> RestoreBackupStreamAsync(Stream stream, BackupImportMode mode);

        /// <summary>
        /// Creates an automated timestamped backup copy of user.db before any restore.
        /// </summary>
        Task<string> CreateLocalSnapshotAsync();

        /// <summary>
        /// Ensures a daily automatic local backup exists in LocalAppData, pruning older backups.
        /// </summary>
        Task<string?> EnsureAutomaticLocalBackupAsync(int retentionCount = 10);
    }
}
