using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.Core.Services
{
    public class ResearchDataBackupService : IResearchDataBackupService
    {
        private readonly IUserRepository _userRepository;
        private readonly ISettingsService _settingsService;
        private readonly string? _databasePath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ResearchDataBackupService(
            IUserRepository userRepository,
            ISettingsService settingsService,
            string? databasePath = null)
        {
            _userRepository = userRepository;
            _settingsService = settingsService;
            _databasePath = databasePath ?? userRepository.DatabasePath;
        }

        public async Task<BackupDataPayload> CreateBackupPayloadAsync()
        {
            var bookmarks = await _userRepository.GetAllBookmarksIncludingDeletedAsync();
            var collections = await _userRepository.GetAllCollectionsIncludingDeletedAsync();
            var highlights = await _userRepository.GetAllHighlightsIncludingDeletedAsync();
            var notes = await _userRepository.GetAllNotesIncludingDeletedAsync();
            var history = await _userRepository.GetAllHistoryAsync();
            var settings = await _settingsService.GetSettingsAsync();

            return new BackupDataPayload
            {
                Bookmarks = bookmarks,
                Collections = collections,
                Highlights = highlights,
                Notes = notes,
                Settings = settings,
                ReadingHistory = history
            };
        }

        public async Task<byte[]> ExportBackupArchiveAsync()
        {
            var payload = await CreateBackupPayloadAsync();
            byte[] dataBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

            using var sha = SHA256.Create();
            byte[] hashBytes = sha.ComputeHash(dataBytes);
            string hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

            string deviceId = await _userRepository.GetDeviceIdAsync();

            var manifest = new BackupManifest
            {
                FormatVersion = 1,
                AppVersion = "1.0.0",
                SchemaVersion = 10,
                ExportedUtc = DateTime.UtcNow,
                DeviceId = deviceId,
                DataSha256 = hashHex,
                CorpusId = Registry.CorpusRegistry.CanonicalCorpusId,
                CorpusVersion = Registry.CorpusRegistry.CanonicalCorpusVersion,
                CorpusSha256 = Registry.CorpusRegistry.ExpectedCanonicalSha256,
                Counts = new BackupCounts
                {
                    Bookmarks = payload.Bookmarks.Count,
                    Collections = payload.Collections.Count,
                    Highlights = payload.Highlights.Count,
                    Notes = payload.Notes.Count,
                    ReadingHistory = payload.ReadingHistory.Count
                }
            };

            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var entryStream = manifestEntry.Open())
                {
                    await entryStream.WriteAsync(manifestBytes);
                }

                var dataEntry = zip.CreateEntry("data.json", CompressionLevel.Optimal);
                using (var entryStream = dataEntry.Open())
                {
                    await entryStream.WriteAsync(dataBytes);
                }
            }

            return ms.ToArray();
        }

        public async Task ExportBackupToFileAsync(string filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var payload = await CreateBackupPayloadAsync();
                byte[] dataBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

                using var sha = SHA256.Create();
                string hashHex = Convert.ToHexString(sha.ComputeHash(dataBytes)).ToLowerInvariant();
                string deviceId = await _userRepository.GetDeviceIdAsync();

                var manifest = new BackupManifest
                {
                    FormatVersion = 1,
                    AppVersion = "1.0.0",
                    SchemaVersion = 10,
                    ExportedUtc = DateTime.UtcNow,
                    DeviceId = deviceId,
                    DataSha256 = hashHex,
                    CorpusId = Registry.CorpusRegistry.CanonicalCorpusId,
                    CorpusVersion = Registry.CorpusRegistry.CanonicalCorpusVersion,
                    CorpusSha256 = Registry.CorpusRegistry.ExpectedCanonicalSha256,
                    Counts = new BackupCounts
                    {
                        Bookmarks = payload.Bookmarks.Count,
                        Collections = payload.Collections.Count,
                        Highlights = payload.Highlights.Count,
                        Notes = payload.Notes.Count,
                        ReadingHistory = payload.ReadingHistory.Count
                    }
                };

                var envelope = new
                {
                    manifest,
                    data = payload
                };

                byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
                await File.WriteAllBytesAsync(filePath, envelopeBytes);
            }
            else
            {
                byte[] zipBytes = await ExportBackupArchiveAsync();
                await File.WriteAllBytesAsync(filePath, zipBytes);
            }
        }

        public async Task<BackupValidationResult> ValidateBackupFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new BackupValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Backup file not found at path: {filePath}"
                };
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await ValidateBackupStreamAsync(fs);
        }

        public async Task<BackupValidationResult> ValidateBackupStreamAsync(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            byte[] header = new byte[4];
            int read = await stream.ReadAsync(header.AsMemory(0, 4));
            if (read < 2)
            {
                return new BackupValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "File is empty or too short to be a valid backup."
                };
            }

            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            // Check if ZIP archive (PK..)
            if (header[0] == 0x50 && header[1] == 0x4B)
            {
                return await ValidateZipArchiveStreamAsync(stream);
            }

            // Check if JSON file
            if (header[0] == (byte)'{' || (header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)) // UTF-8 BOM or '{'
            {
                return await ValidateJsonStreamAsync(stream);
            }

            return new BackupValidationResult
            {
                IsValid = false,
                ErrorMessage = "Unrecognized backup file format. Expected a .vdbbackup ZIP archive or JSON backup."
            };
        }

        private static async Task<BackupValidationResult> ValidateZipArchiveStreamAsync(Stream stream)
        {
            try
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                
                var manifestEntry = zip.GetEntry("manifest.json");
                if (manifestEntry == null)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Backup archive is corrupt: missing manifest.json."
                    };
                }

                BackupManifest? manifest;
                using (var mStream = manifestEntry.Open())
                {
                    manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(mStream, JsonOptions);
                }

                if (manifest == null)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Failed to deserialize backup manifest."
                    };
                }

                if (manifest.FormatVersion != 1)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Unsupported backup format version: {manifest.FormatVersion} (supported: 1)."
                    };
                }

                if (manifest.SchemaVersion > 11)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Backup has schema version {manifest.SchemaVersion}, which is newer than the current supported version (11)."
                    };
                }

                var dataEntry = zip.GetEntry("data.json");
                if (dataEntry == null)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Backup archive is corrupt: missing data.json."
                    };
                }

                byte[] dataBytes;
                using (var dStream = dataEntry.Open())
                using (var ms = new MemoryStream())
                {
                    await dStream.CopyToAsync(ms);
                    dataBytes = ms.ToArray();
                }

                // Checksum verification
                using var sha = SHA256.Create();
                byte[] computedHashBytes = sha.ComputeHash(dataBytes);
                string computedHashHex = Convert.ToHexString(computedHashBytes);

                if (!string.Equals(computedHashHex, manifest.DataSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Checksum verification failed: payload has been modified or corrupted (expected {manifest.DataSha256}, got {computedHashHex})."
                    };
                }

                BackupDataPayload? payload = JsonSerializer.Deserialize<BackupDataPayload>(dataBytes, JsonOptions);
                if (payload == null)
                {
                    return new BackupValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Failed to parse data.json payload."
                    };
                }

                string? corpusWarning = null;
                bool isCorpusCompatible = true;
                if (!string.IsNullOrEmpty(manifest.CorpusId) && !string.Equals(manifest.CorpusId, Registry.CorpusRegistry.CanonicalCorpusId, StringComparison.OrdinalIgnoreCase))
                {
                    isCorpusCompatible = false;
                    corpusWarning = $"Backup references corpus '{manifest.CorpusId}', which differs from the active canonical corpus '{Registry.CorpusRegistry.CanonicalCorpusId}'.";
                }

                return new BackupValidationResult
                {
                    IsValid = true,
                    Manifest = manifest,
                    Payload = payload,
                    IsCorpusCompatible = isCorpusCompatible,
                    CorpusCompatibilityWarning = corpusWarning
                };
            }
            catch (Exception ex)
            {
                return new BackupValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Archive reading error: {ex.Message}"
                };
            }
        }

        private static async Task<BackupValidationResult> ValidateJsonStreamAsync(Stream stream)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                // Check if envelope with "manifest" and "data"
                if (root.TryGetProperty("manifest", out var mProp) && root.TryGetProperty("data", out var dProp))
                {
                    var manifest = JsonSerializer.Deserialize<BackupManifest>(mProp.GetRawText(), JsonOptions);
                    var payload = JsonSerializer.Deserialize<BackupDataPayload>(dProp.GetRawText(), JsonOptions);

                    if (manifest == null || payload == null)
                    {
                        return new BackupValidationResult { IsValid = false, ErrorMessage = "Failed to parse JSON backup envelope." };
                    }

                    if (manifest.FormatVersion != 1)
                    {
                        return new BackupValidationResult { IsValid = false, ErrorMessage = $"Unsupported backup format version: {manifest.FormatVersion}." };
                    }

                    if (manifest.SchemaVersion > 11)
                    {
                        return new BackupValidationResult { IsValid = false, ErrorMessage = $"Unsupported schema version: {manifest.SchemaVersion}." };
                    }

                    string? corpusWarning = null;
                    bool isCorpusCompatible = true;
                    if (!string.IsNullOrEmpty(manifest.CorpusId) && !string.Equals(manifest.CorpusId, Registry.CorpusRegistry.CanonicalCorpusId, StringComparison.OrdinalIgnoreCase))
                    {
                        isCorpusCompatible = false;
                        corpusWarning = $"Backup references corpus '{manifest.CorpusId}', which differs from the active canonical corpus '{Registry.CorpusRegistry.CanonicalCorpusId}'.";
                    }

                    return new BackupValidationResult
                    {
                        IsValid = true,
                        Manifest = manifest,
                        Payload = payload,
                        IsCorpusCompatible = isCorpusCompatible,
                        CorpusCompatibilityWarning = corpusWarning
                    };
                }

                // Direct BackupDataPayload
                var directPayload = JsonSerializer.Deserialize<BackupDataPayload>(root.GetRawText(), JsonOptions);
                if (directPayload == null)
                {
                    return new BackupValidationResult { IsValid = false, ErrorMessage = "Invalid JSON backup data format." };
                }

                var defaultManifest = new BackupManifest
                {
                    FormatVersion = 1,
                    AppVersion = "1.0.0",
                    SchemaVersion = 10,
                    ExportedUtc = DateTime.UtcNow,
                    Counts = new BackupCounts
                    {
                        Bookmarks = directPayload.Bookmarks?.Count ?? 0,
                        Collections = directPayload.Collections?.Count ?? 0,
                        Highlights = directPayload.Highlights?.Count ?? 0,
                        Notes = directPayload.Notes?.Count ?? 0,
                        ReadingHistory = directPayload.ReadingHistory?.Count ?? 0
                    }
                };

                return new BackupValidationResult
                {
                    IsValid = true,
                    Manifest = defaultManifest,
                    Payload = directPayload
                };
            }
            catch (Exception ex)
            {
                return new BackupValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"JSON parsing error: {ex.Message}"
                };
            }
        }

        public async Task<string> CreateLocalSnapshotAsync()
        {
            return await Task.Run(() =>
            {
                string dbPath = _databasePath ?? _userRepository.DatabasePath;
                if (!File.Exists(dbPath))
                {
                    return string.Empty;
                }

                string backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "Backups");
                Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string snapshotPath = Path.Combine(backupDir, $"user.db.pre_restore_{timestamp}.bak");
                if (File.Exists(snapshotPath))
                {
                    snapshotPath = Path.Combine(backupDir, $"user.db.pre_restore_{timestamp}_{Guid.NewGuid().ToString("N")[..6]}.bak");
                }

                File.Copy(dbPath, snapshotPath, overwrite: true);
                return snapshotPath;
            });
        }

        public async Task<BackupRestoreResult> RestoreBackupAsync(string filePath, BackupImportMode mode)
        {
            var validation = await ValidateBackupFileAsync(filePath);
            if (!validation.IsValid || validation.Payload == null)
            {
                return new BackupRestoreResult
                {
                    Success = false,
                    ErrorMessage = validation.ErrorMessage ?? "Validation failed."
                };
            }

            string snapshotPath = string.Empty;
            try
            {
                snapshotPath = await CreateLocalSnapshotAsync();
            }
            catch (Exception ex)
            {
                return new BackupRestoreResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to create safety snapshot before restoring: {ex.Message}"
                };
            }

            try
            {
                if (mode == BackupImportMode.Replace)
                {
                    await _userRepository.RestoreDataAsync(validation.Payload);
                }
                else
                {
                    await _userRepository.MergeDataAsync(validation.Payload);
                }

                return new BackupRestoreResult
                {
                    Success = true,
                    SnapshotPath = snapshotPath,
                    ImportedCounts = validation.Manifest?.Counts
                };
            }
            catch (Exception ex)
            {
                return new BackupRestoreResult
                {
                    Success = false,
                    SnapshotPath = snapshotPath,
                    ErrorMessage = $"Restore failed: {ex.Message}"
                };
            }
        }

        public async Task<BackupRestoreResult> RestoreBackupStreamAsync(Stream stream, BackupImportMode mode)
        {
            var validation = await ValidateBackupStreamAsync(stream);
            if (!validation.IsValid || validation.Payload == null)
            {
                return new BackupRestoreResult
                {
                    Success = false,
                    ErrorMessage = validation.ErrorMessage ?? "Validation failed."
                };
            }

            string snapshotPath = string.Empty;
            try
            {
                snapshotPath = await CreateLocalSnapshotAsync();
            }
            catch (Exception ex)
            {
                return new BackupRestoreResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to create safety snapshot before restoring: {ex.Message}"
                };
            }

            try
            {
                if (mode == BackupImportMode.Replace)
                {
                    await _userRepository.RestoreDataAsync(validation.Payload);
                }
                else
                {
                    await _userRepository.MergeDataAsync(validation.Payload);
                }

                return new BackupRestoreResult
                {
                    Success = true,
                    SnapshotPath = snapshotPath,
                    ImportedCounts = validation.Manifest?.Counts
                };
            }
            catch (Exception ex)
            {
                return new BackupRestoreResult
                {
                    Success = false,
                    SnapshotPath = snapshotPath,
                    ErrorMessage = $"Restore failed: {ex.Message}"
                };
            }
        }

        public async Task<string?> EnsureAutomaticLocalBackupAsync(int retentionCount = 10)
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string backupDir = Path.Combine(localApp, "VedaBaseModern", "Backups");
                Directory.CreateDirectory(backupDir);

                var existing = Directory.GetFiles(backupDir, "*.vdbbackup")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToList();

                // If a backup was already created within the last 24 hours, skip creation
                if (existing.Count > 0 && (DateTime.UtcNow - existing[0].CreationTimeUtc).TotalHours < 24)
                {
                    return existing[0].FullName;
                }

                string fileName = $"vedabase_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.vdbbackup";
                string targetPath = Path.Combine(backupDir, fileName);

                await ExportBackupToFileAsync(targetPath);

                // Prune rolling backups older than retentionCount
                var updated = Directory.GetFiles(backupDir, "*.vdbbackup")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToList();

                if (updated.Count > retentionCount)
                {
                    foreach (var old in updated.Skip(retentionCount))
                    {
                        try { old.Delete(); } catch { }
                    }
                }

                return targetPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Automatic backup failed: {ex}");
                return null;
            }
        }
    }
}
