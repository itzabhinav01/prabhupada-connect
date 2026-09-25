using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.Core.Services
{
    public class ResearchSyncService : IResearchSyncService
    {
        private readonly IUserRepository _userRepository;
        private readonly IResearchDataBackupService _backupService;
        private readonly ICredentialStorageService _credentialStorage;
        private readonly HttpClient _httpClient;

        private ISyncProvider? _currentProvider;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private bool _isSyncing;
        private Timer? _periodicTimer;
        private int _syncIntervalMinutes;

        public ISyncProvider? CurrentProvider => _currentProvider;

        public ResearchSyncService(
            IUserRepository userRepository,
            IResearchDataBackupService backupService,
            ICredentialStorageService credentialStorage,
            HttpClient? httpClient = null,
            ISyncProvider? initialProvider = null)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
            _credentialStorage = credentialStorage ?? throw new ArgumentNullException(nameof(credentialStorage));
            _httpClient = httpClient ?? new HttpClient();
            _currentProvider = initialProvider;
        }

        public void SetCustomProvider(ISyncProvider provider)
        {
            _currentProvider = provider;
        }

        /// <summary>
        /// Runs one sync step (a pull or a push) with bounded, specific
        /// recovery (Phase 7) - never an open-ended retry loop:
        ///   - HTTP 401: re-authenticate once via the provider's stored
        ///     credentials, then retry the SAME step exactly once. Covers
        ///     the common "JWT expired mid-session" case without requiring
        ///     the user to reconnect or restart the app.
        ///   - HTTP 429 or 5xx: wait a short fixed delay, retry exactly
        ///     once. Covers a transient rate-limit or server blip.
        ///   - Anything else (400/403/404/malformed JSON/cancellation/etc.):
        ///     no retry - fails immediately, exactly as before Phase 7.
        /// </summary>
        private async Task<T> ExecuteWithRecoveryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && _currentProvider != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool reauthed = await _currentProvider.AuthenticateAsync();
                if (!reauthed) throw;
                return await operation();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests || (int?)ex.StatusCode >= 500)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                return await operation();
            }
        }

        private static SyncErrorCategory CategorizeError(Exception ex)
        {
            if (ex is OperationCanceledException) return SyncErrorCategory.Canceled;
            if (ex is HttpRequestException httpEx)
            {
                return httpEx.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => SyncErrorCategory.Authentication,
                    HttpStatusCode.Forbidden => SyncErrorCategory.Permission,
                    HttpStatusCode.TooManyRequests => SyncErrorCategory.RateLimited,
                    HttpStatusCode.InternalServerError => SyncErrorCategory.ServerError,
                    null => SyncErrorCategory.Network,
                    _ when (int)httpEx.StatusCode >= 500 => SyncErrorCategory.ServerError,
                    _ => SyncErrorCategory.Other
                };
            }
            if (ex is TaskCanceledException) return SyncErrorCategory.Network; // HttpClient timeout surfaces as this
            return SyncErrorCategory.Other;
        }

        private async Task EnsureProviderInitializedAsync()
        {
            if (_currentProvider != null) return;

            string? providerType = await _userRepository.GetSyncMetadataAsync("SyncProviderType");
            if (string.Equals(providerType, "Supabase", StringComparison.OrdinalIgnoreCase))
            {
                string? url = await _userRepository.GetSyncMetadataAsync("SupabaseProjectUrl");
                string? email = await _userRepository.GetSyncMetadataAsync("SupabaseUserEmail");
                string? apiKey = await _credentialStorage.GetCredentialsAsync("Supabase_ApiKey");
                string? password = await _credentialStorage.GetCredentialsAsync("Supabase_Password");
                string? authToken = await _credentialStorage.GetCredentialsAsync("Supabase_AuthToken");
                string? userId = await _credentialStorage.GetCredentialsAsync("Supabase_UserId");
                string deviceId = await _userRepository.GetDeviceIdAsync();

                if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(apiKey))
                {
                    var config = new SupabaseConfig
                    {
                        ProjectUrl = url,
                        AnonKey = apiKey,
                        UserEmail = email,
                        UserPassword = password,
                        AuthToken = authToken,
                        UserId = userId
                    };

                    _currentProvider = new SupabaseSyncProvider(_httpClient, config, deviceId);

                    // If periodic sync is configured, start timer
                    string? intervalStr = await _userRepository.GetSyncMetadataAsync("SyncIntervalMinutes");
                    if (int.TryParse(intervalStr, out int interval) && interval > 0)
                    {
                        _syncIntervalMinutes = interval;
                        StartPeriodicSync();
                    }
                }
            }
        }

        public async Task<SyncStatusInfo> GetSyncStatusAsync()
        {
            await EnsureProviderInitializedAsync();

            string providerType = await _userRepository.GetSyncMetadataAsync("SyncProviderType") ?? "None";
            string? lastSyncStr = await _userRepository.GetSyncMetadataAsync("LastSyncUtc");
            string lastResult = await _userRepository.GetSyncMetadataAsync("LastSyncResult") ?? string.Empty;
            string lastMsg = await _userRepository.GetSyncMetadataAsync("LastSyncMessage") ?? string.Empty;
            string? projectUrl = await _userRepository.GetSyncMetadataAsync("SupabaseProjectUrl");
            string? userEmail = await _userRepository.GetSyncMetadataAsync("SupabaseUserEmail");

            _ = int.TryParse(await _userRepository.GetSyncMetadataAsync("LastSyncUploaded") ?? "0", out int uploaded);
            _ = int.TryParse(await _userRepository.GetSyncMetadataAsync("LastSyncDownloaded") ?? "0", out int downloaded);
            _ = int.TryParse(await _userRepository.GetSyncMetadataAsync("LastSyncConflicts") ?? "0", out int conflicts);
            _ = int.TryParse(await _userRepository.GetSyncMetadataAsync("SyncIntervalMinutes") ?? "0", out int intervalMinutes);
            _ = Enum.TryParse<SyncErrorCategory>(await _userRepository.GetSyncMetadataAsync("LastSyncErrorCategory") ?? "None", out var errorCategory);

            DateTime? lastSyncUtc = null;
            if (!string.IsNullOrEmpty(lastSyncStr) && DateTime.TryParse(lastSyncStr, out var dt))
            {
                lastSyncUtc = dt.ToUniversalTime();
            }

            int pendingLocal = 0;
            try
            {
                var pendingChanges = await _userRepository.GetLocalChangesAsync(lastSyncUtc);
                pendingLocal = pendingChanges.TotalCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Failed to compute pending local changes: {ex.Message}");
            }

            return new SyncStatusInfo
            {
                IsConfigured = _currentProvider != null || (providerType != "None" && !string.IsNullOrEmpty(projectUrl)),
                ProviderType = providerType,
                LastSyncUtc = lastSyncUtc,
                LastSyncResult = lastResult,
                LastSyncMessage = lastMsg,
                UploadedCount = uploaded,
                DownloadedCount = downloaded,
                ConflictCount = conflicts,
                IsSyncing = _isSyncing,
                ProjectUrl = projectUrl,
                UserEmail = userEmail,
                SyncIntervalMinutes = intervalMinutes,
                PendingLocalChanges = pendingLocal,
                LastErrorCategory = errorCategory
            };
        }

        public async Task<SyncDiagnostics> GetDiagnosticsAsync()
        {
            var status = await GetSyncStatusAsync();
            string? lastAttemptedStr = await _userRepository.GetSyncMetadataAsync("LastAttemptedSyncUtc");
            DateTime? lastAttemptedUtc = null;
            if (!string.IsNullOrEmpty(lastAttemptedStr) && DateTime.TryParse(lastAttemptedStr, out var dtAttempt))
            {
                lastAttemptedUtc = dtAttempt.ToUniversalTime();
            }
            double? durationSeconds = null;
            string? durationStr = await _userRepository.GetSyncMetadataAsync("LastSyncDurationSeconds");
            if (double.TryParse(durationStr, out var parsedDuration)) durationSeconds = parsedDuration;

            return new SyncDiagnostics
            {
                State = status.State,
                ProviderType = status.ProviderType,
                LastSuccessfulSyncUtc = status.LastSyncUtc,
                LastAttemptedSyncUtc = lastAttemptedUtc,
                LastSyncDurationSeconds = durationSeconds,
                RecordsUploaded = status.UploadedCount,
                RecordsDownloaded = status.DownloadedCount,
                ConflictsResolved = status.ConflictCount,
                PendingLocalChanges = status.PendingLocalChanges,
                LastErrorCategory = status.LastErrorCategory,
                LastErrorMessage = status.LastErrorCategory != SyncErrorCategory.None ? status.LastSyncMessage : null,
                ProjectUrl = status.ProjectUrl,
                UserEmail = status.UserEmail,
                SyncIntervalMinutes = status.SyncIntervalMinutes
            };
        }

        public async Task ExportDiagnosticsAsync(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            var diagnostics = await GetDiagnosticsAsync();

            // Field-by-field serialization (not a generic serializer over the
            // whole object graph) so it is structurally impossible for a
            // future field added elsewhere to accidentally leak into this
            // file - every value written here is independently known to be
            // non-secret (see SyncDiagnostics' own doc comment).
            var safeExport = new
            {
                state = diagnostics.State.ToString(),
                provider = diagnostics.ProviderType,
                projectUrl = diagnostics.ProjectUrl,
                userEmail = diagnostics.UserEmail,
                lastSuccessfulSyncUtc = diagnostics.LastSuccessfulSyncUtc?.ToString("O"),
                lastAttemptedSyncUtc = diagnostics.LastAttemptedSyncUtc?.ToString("O"),
                lastSyncDurationSeconds = diagnostics.LastSyncDurationSeconds,
                uploaded = diagnostics.RecordsUploaded,
                downloaded = diagnostics.RecordsDownloaded,
                conflictsResolved = diagnostics.ConflictsResolved,
                pendingLocalChanges = diagnostics.PendingLocalChanges,
                syncIntervalMinutes = diagnostics.SyncIntervalMinutes,
                errorCategory = diagnostics.LastErrorCategory.ToString(),
                error = diagnostics.LastErrorMessage
            };

            string json = JsonSerializer.Serialize(safeExport, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<bool> WouldSwitchProjectAsync(string projectUrl, string? userEmail)
        {
            string? prevUrl = await _userRepository.GetSyncMetadataAsync("SupabaseProjectUrl");
            string? prevEmail = await _userRepository.GetSyncMetadataAsync("SupabaseUserEmail");

            // Nothing configured yet - connecting now is a first-time
            // connection, never a "switch".
            if (string.IsNullOrEmpty(prevUrl)) return false;

            bool urlChanged = !string.Equals(prevUrl, projectUrl, StringComparison.OrdinalIgnoreCase);
            bool emailChanged = !string.Equals(prevEmail, userEmail, StringComparison.OrdinalIgnoreCase);
            return urlChanged || emailChanged;
        }

        public async Task<SyncConnectionTestResult> ConfigureSupabaseAsync(
            string projectUrl,
            string apiKey,
            string? userEmail = null,
            string? userPassword = null)
        {
            if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(apiKey))
            {
                return new SyncConnectionTestResult
                {
                    Success = false,
                    ErrorMessage = "Project URL and API Key are required."
                };
            }

            string deviceId = await _userRepository.GetDeviceIdAsync();
            var testConfig = new SupabaseConfig
            {
                ProjectUrl = projectUrl,
                AnonKey = apiKey,
                UserEmail = userEmail,
                UserPassword = userPassword
            };

            var testProvider = new SupabaseSyncProvider(_httpClient, testConfig, deviceId);
            var testResult = await testProvider.TestConnectionAsync();

            if (testResult.Success)
            {
                // Check if project target changed; if so, clear LastSyncUtc to guarantee clean initial sync
                string? prevUrl = await _userRepository.GetSyncMetadataAsync("SupabaseProjectUrl");
                string? prevEmail = await _userRepository.GetSyncMetadataAsync("SupabaseUserEmail");
                if (!string.Equals(prevUrl, projectUrl, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(prevEmail, userEmail, StringComparison.OrdinalIgnoreCase))
                {
                    await _userRepository.SetSyncMetadataAsync("LastSyncUtc", string.Empty);
                }

                await _credentialStorage.SaveCredentialsAsync("Supabase_ApiKey", apiKey);
                if (!string.IsNullOrEmpty(userPassword))
                {
                    await _credentialStorage.SaveCredentialsAsync("Supabase_Password", userPassword);
                }
                else
                {
                    await _credentialStorage.DeleteCredentialsAsync("Supabase_Password");
                }

                if (!string.IsNullOrEmpty(testConfig.AuthToken))
                {
                    await _credentialStorage.SaveCredentialsAsync("Supabase_AuthToken", testConfig.AuthToken);
                }
                if (!string.IsNullOrEmpty(testConfig.UserId))
                {
                    await _credentialStorage.SaveCredentialsAsync("Supabase_UserId", testConfig.UserId);
                }

                await _userRepository.SetSyncMetadataAsync("SupabaseProjectUrl", projectUrl);
                await _userRepository.SetSyncMetadataAsync("SupabaseUserEmail", userEmail ?? string.Empty);
                await _userRepository.SetSyncMetadataAsync("SyncProviderType", "Supabase");
                _currentProvider = testProvider;

                // Start periodic sync if an interval was previously set
                string? intervalStr = await _userRepository.GetSyncMetadataAsync("SyncIntervalMinutes");
                if (int.TryParse(intervalStr, out int interval) && interval > 0)
                {
                    _syncIntervalMinutes = interval;
                    StartPeriodicSync();
                }
            }

            return testResult;
        }

        public async Task SetSyncIntervalAsync(int intervalMinutes)
        {
            _syncIntervalMinutes = intervalMinutes;
            await _userRepository.SetSyncMetadataAsync("SyncIntervalMinutes", intervalMinutes.ToString());

            if (intervalMinutes > 0)
            {
                StartPeriodicSync();
            }
            else
            {
                StopPeriodicSync();
            }
        }

        public void StartPeriodicSync()
        {
            StopPeriodicSync();
            if (_syncIntervalMinutes <= 0) return;

            var interval = TimeSpan.FromMinutes(_syncIntervalMinutes);
            _periodicTimer = new Timer(async _ =>
            {
                await OnPeriodicSyncTickAsync();
            }, null, interval, interval);
        }

        public void StopPeriodicSync()
        {
            _periodicTimer?.Dispose();
            _periodicTimer = null;
        }

        private async Task OnPeriodicSyncTickAsync()
        {
            if (_isSyncing) return;

            try
            {
                // Run background delta sync silently
                await SyncNowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PeriodicSync] Tick failed: {ex.Message}");
            }
        }

        public async Task DisconnectProviderAsync()
        {
            StopPeriodicSync();

            if (_currentProvider != null)
            {
                await _currentProvider.DisconnectAsync();
                _currentProvider = null;
            }

            await _credentialStorage.DeleteCredentialsAsync("Supabase_ApiKey");
            await _credentialStorage.DeleteCredentialsAsync("Supabase_Password");
            await _credentialStorage.DeleteCredentialsAsync("Supabase_AuthToken");
            await _credentialStorage.DeleteCredentialsAsync("Supabase_UserId");

            await _userRepository.SetSyncMetadataAsync("SyncProviderType", "None");
            await _userRepository.SetSyncMetadataAsync("SupabaseProjectUrl", string.Empty);
            await _userRepository.SetSyncMetadataAsync("SupabaseUserEmail", string.Empty);
            await _userRepository.SetSyncMetadataAsync("LastSyncResult", "Disconnected");
            await _userRepository.SetSyncMetadataAsync("LastSyncMessage", "Cloud sync disconnected. Local database preserved intact.");
        }

        public async Task<SyncPreviewResult> PreviewSyncAsync()
        {
            await EnsureProviderInitializedAsync();
            if (_currentProvider == null)
            {
                return new SyncPreviewResult { ErrorMessage = "No cloud provider is currently connected." };
            }

            try
            {
                string? lastSyncStr = await _userRepository.GetSyncMetadataAsync("LastSyncUtc");
                DateTime? lastSyncUtc = null;
                if (!string.IsNullOrEmpty(lastSyncStr) && DateTime.TryParse(lastSyncStr, out var dt))
                {
                    lastSyncUtc = dt.ToUniversalTime();
                }

                var localChanges = await _userRepository.GetLocalChangesAsync(lastSyncUtc);
                var remoteChanges = await _currentProvider.PullChangesAsync(lastSyncUtc);

                return new SyncPreviewResult
                {
                    LocalChangesCount = localChanges.TotalCount,
                    RemoteChangesCount = remoteChanges.TotalCount
                };
            }
            catch (Exception ex)
            {
                return new SyncPreviewResult { ErrorMessage = $"Preview failed: {ex.Message}" };
            }
        }

        public async Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
        {
            await EnsureProviderInitializedAsync();
            if (_currentProvider == null)
            {
                return new SyncResult
                {
                    Success = false,
                    ErrorMessage = "No sync provider configured. Connect a Supabase project first."
                };
            }

            try
            {
                if (!await _syncLock.WaitAsync(0, cancellationToken))
                {
                    return new SyncResult
                    {
                        Success = false,
                        ErrorMessage = "A synchronization operation is already in progress."
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return new SyncResult
                {
                    Success = false,
                    ErrorMessage = "Synchronization operation was canceled."
                };
            }

            _isSyncing = true;
            string snapshotPath = string.Empty;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Capture safety snapshot before any sync mutation
                try
                {
                    snapshotPath = await _backupService.CreateLocalSnapshotAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Sync] Safety snapshot failed: {ex.Message}");
                }

                // 2. Read sync high-water mark
                string? lastSyncStr = await _userRepository.GetSyncMetadataAsync("LastSyncUtc");
                DateTime? lastSyncUtc = null;
                if (!string.IsNullOrEmpty(lastSyncStr) && DateTime.TryParse(lastSyncStr, out var dt))
                {
                    lastSyncUtc = dt.ToUniversalTime();
                }

                var syncStartUtc = DateTime.UtcNow;

                cancellationToken.ThrowIfCancellationRequested();
                await _userRepository.SetSyncMetadataAsync("LastAttemptedSyncUtc", syncStartUtc.ToString("O"));

                // 3. Pull remote changes (bounded 401/429/5xx recovery - see ExecuteWithRecoveryAsync)
                var remoteChanges = await ExecuteWithRecoveryAsync(
                    () => _currentProvider.PullChangesAsync(lastSyncUtc), cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                // 4. Merge remote changes into local SQLite (LWW + verse uniqueness)
                string deviceId = await _userRepository.GetDeviceIdAsync();
                int conflicts = await _userRepository.MergeSyncChangesAsync(remoteChanges, deviceId);

                // 5. Query local pending changes to push
                var localChanges = await _userRepository.GetLocalChangesAsync(lastSyncUtc);

                cancellationToken.ThrowIfCancellationRequested();

                // 6. Push local changes to remote (same bounded recovery)
                int pushed = await ExecuteWithRecoveryAsync(
                    () => _currentProvider.PushChangesAsync(localChanges), cancellationToken);

                // 7. Update sync metadata - checkpoint is only advanced here,
                // after every prior step has fully succeeded (rule: a failed
                // sync must never mark local data as successfully synced).
                double durationSeconds = (DateTime.UtcNow - syncStartUtc).TotalSeconds;
                await _userRepository.SetSyncMetadataAsync("LastSyncUtc", syncStartUtc.ToString("O"));
                await _userRepository.SetSyncMetadataAsync("LastSyncResult", "Success");
                await _userRepository.SetSyncMetadataAsync("LastSyncErrorCategory", SyncErrorCategory.None.ToString());
                await _userRepository.SetSyncMetadataAsync("LastSyncUploaded", pushed.ToString());
                await _userRepository.SetSyncMetadataAsync("LastSyncDownloaded", remoteChanges.TotalCount.ToString());
                await _userRepository.SetSyncMetadataAsync("LastSyncConflicts", conflicts.ToString());
                await _userRepository.SetSyncMetadataAsync("LastSyncDurationSeconds", durationSeconds.ToString("F2"));
                await _userRepository.SetSyncMetadataAsync("LastSyncMessage", $"Synced: {pushed} uploaded, {remoteChanges.TotalCount} downloaded, {conflicts} conflicts resolved.");

                return new SyncResult
                {
                    Success = true,
                    UploadedCount = pushed,
                    DownloadedCount = remoteChanges.TotalCount,
                    ConflictCount = conflicts,
                    SnapshotPath = snapshotPath
                };
            }
            catch (OperationCanceledException)
            {
                // Checkpoint (LastSyncUtc) is deliberately NOT touched - an
                // interrupted sync must not be mistaken for a completed one,
                // and any partial local merge already committed by SQLite
                // transactions up to the cancellation point is still valid,
                // consistent data (never a half-written row).
                await _userRepository.SetSyncMetadataAsync("LastSyncResult", "Canceled");
                await _userRepository.SetSyncMetadataAsync("LastSyncErrorCategory", SyncErrorCategory.Canceled.ToString());
                await _userRepository.SetSyncMetadataAsync("LastSyncMessage", "Synchronization operation was canceled.");

                return new SyncResult
                {
                    Success = false,
                    ErrorMessage = "Synchronization operation was canceled.",
                    SnapshotPath = snapshotPath
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Sync failed: {ex}");
                var category = CategorizeError(ex);
                await _userRepository.SetSyncMetadataAsync("LastSyncResult", "Error");
                await _userRepository.SetSyncMetadataAsync("LastSyncErrorCategory", category.ToString());
                await _userRepository.SetSyncMetadataAsync("LastSyncMessage", $"Sync failed: {ex.Message}");

                return new SyncResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    SnapshotPath = snapshotPath
                };
            }
            finally
            {
                _isSyncing = false;
                _syncLock.Release();
            }
        }
    }
}
