using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VedaBaseModern.Core.Services
{
    public class AppUpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "2.0.0";
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseTitle { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? PatchDownloadUrl { get; set; }
        public bool IsPatchAvailable => !string.IsNullOrEmpty(PatchDownloadUrl);
        public long PatchSizeBytes { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public bool CheckSucceeded { get; set; }
    }

    public interface IAppUpdateService
    {
        Task<AppUpdateInfo> CheckForUpdatesAsync(string? currentVersionOverride = null);
        Task<(bool Success, string Message)> DownloadAndApplyPatchAsync(
            string patchUrl,
            string? targetDirectory = null,
            IProgress<(double Percentage, string Status)>? progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Checks the official GitHub repository for updates and release installers.
    /// Safely handles offline states, rate limits, non-existing releases, and
    /// downloads / applies lightweight in-place binary patches (Update.zip).
    /// </summary>
    public class AppUpdateService : IAppUpdateService
    {
        public const string GitHubApiUrl = "https://api.github.com/repos/itzabhinav01/prabhupada-connect/releases/latest";
        public const string FallbackReleaseUrl = "https://github.com/itzabhinav01/prabhupada-connect/releases";
        private readonly HttpClient _httpClient;

        public AppUpdateService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<AppUpdateInfo> CheckForUpdatesAsync(string? currentVersionOverride = null)
        {
            string currentVersion = currentVersionOverride ?? 
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2.0.0";

            var info = new AppUpdateInfo
            {
                CurrentVersion = currentVersion,
                ReleaseUrl = FallbackReleaseUrl
            };

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("PrabhupadaConnect", "2.0"));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var resp = await _httpClient.SendAsync(req);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // No releases published yet on this repository
                    info.CheckSucceeded = true;
                    info.IsUpdateAvailable = false;
                    info.LatestVersion = currentVersion;
                    info.StatusMessage = $"You are running the latest version (v{currentVersion}). No newer releases found on GitHub.";
                    return info;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    info.CheckSucceeded = false;
                    info.StatusMessage = $"GitHub release check returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}).";
                    return info;
                }

                string json = await resp.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<GitHubReleaseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                {
                    info.CheckSucceeded = true;
                    info.IsUpdateAvailable = false;
                    info.LatestVersion = currentVersion;
                    info.StatusMessage = $"You are running the latest version (v{currentVersion}).";
                    return info;
                }

                string rawTag = release.TagName.Trim();
                string cleanTag = rawTag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? rawTag[1..] : rawTag;
                info.LatestVersion = cleanTag;
                info.ReleaseTitle = release.Name ?? $"Version {rawTag}";
                info.ReleaseNotes = release.Body ?? string.Empty;
                info.ReleaseUrl = !string.IsNullOrWhiteSpace(release.HtmlUrl) ? release.HtmlUrl : FallbackReleaseUrl;

                // 1. Look for lightweight patch archive (.zip)
                var patchAsset = release.Assets?.FirstOrDefault(a => 
                    a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    (a.Name.Contains("update", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("patch", StringComparison.OrdinalIgnoreCase)));

                if (patchAsset != null)
                {
                    info.PatchDownloadUrl = patchAsset.BrowserDownloadUrl;
                    info.PatchSizeBytes = patchAsset.Size;
                }

                // 2. Look for direct executable installer in release assets (.exe)
                var exeAsset = release.Assets?.FirstOrDefault(a => 
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    a.BrowserDownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                info.DownloadUrl = exeAsset?.BrowserDownloadUrl ?? info.ReleaseUrl;

                bool isNewer = CompareVersions(cleanTag, currentVersion);
                info.CheckSucceeded = true;
                info.IsUpdateAvailable = isNewer;

                if (isNewer)
                {
                    string sizeInfo = info.PatchSizeBytes > 0 
                        ? $" (~{(info.PatchSizeBytes / (1024.0 * 1024.0)):0.0} MB)"
                        : string.Empty;
                    info.StatusMessage = info.IsPatchAvailable
                        ? $"Update available: v{cleanTag} ready for fast in-app update{sizeInfo}!"
                        : $"Update available: v{cleanTag} is ready for download!";
                }
                else
                {
                    info.StatusMessage = $"You are running the latest version (v{currentVersion}).";
                }

                return info;
            }
            catch (HttpRequestException ex)
            {
                info.CheckSucceeded = false;
                info.StatusMessage = "Unable to connect to GitHub. Please check your internet connection.";
                System.Diagnostics.Debug.WriteLine($"[Update] HTTP error: {ex.Message}");
                return info;
            }
            catch (Exception ex)
            {
                info.CheckSucceeded = false;
                info.StatusMessage = $"Failed to check for updates: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[Update] Error: {ex}");
                return info;
            }
        }

        public async Task<(bool Success, string Message)> DownloadAndApplyPatchAsync(
            string patchUrl,
            string? targetDirectory = null,
            IProgress<(double Percentage, string Status)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(patchUrl))
            {
                return (false, "Invalid update download URL.");
            }

            try
            {
                targetDirectory ??= AppContext.BaseDirectory;

                string tempDir = Path.Combine(Path.GetTempPath(), "PrabhupadaConnect_Update");
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
                Directory.CreateDirectory(tempDir);

                string zipPath = Path.Combine(tempDir, "update.zip");
                string stageDir = Path.Combine(tempDir, "staged");

                progress?.Report((5, "Connecting to download server..."));

                using (var req = new HttpRequestMessage(HttpMethod.Get, patchUrl))
                {
                    req.Headers.UserAgent.Add(new ProductInfoHeaderValue("PrabhupadaConnect", "2.0"));
                    using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    long totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    long totalRead = 0L;

                    await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            totalRead += read;
                            if (totalBytes > 0)
                            {
                                double pct = 5.0 + ((double)totalRead / totalBytes) * 75.0; // 5% to 80%
                                string mbRead = (totalRead / (1024.0 * 1024.0)).ToString("0.0");
                                string mbTotal = (totalBytes / (1024.0 * 1024.0)).ToString("0.0");
                                progress?.Report((pct, $"Downloading update ({mbRead} MB / {mbTotal} MB)..."));
                            }
                            else
                            {
                                progress?.Report((40, $"Downloading update ({totalRead / 1024} KB)..."));
                            }
                        }
                    }
                }

                progress?.Report((85, "Extracting update package..."));
                Directory.CreateDirectory(stageDir);
                ZipFile.ExtractToDirectory(zipPath, stageDir, overwriteFiles: true);

                progress?.Report((90, "Applying updated files..."));

                // Clean up any .old files from prior updates
                CleanUpOldFiles(targetDirectory);

                // Apply files in-place using atomic rename pattern for locked running files
                CopyDirectoryWithRename(stageDir, targetDirectory);

                progress?.Report((98, "Restarting application..."));

                // Launch the updated application cleanly
                string exePath = Path.Combine(targetDirectory, "VedaBaseModern2.UI.exe");
                if (File.Exists(exePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = targetDirectory,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }

                // Exit current process cleanly
                _ = Task.Run(async () =>
                {
                    await Task.Delay(400);
                    Environment.Exit(0);
                });

                return (true, "Update applied successfully. Restarting...");
            }
            catch (OperationCanceledException)
            {
                return (false, "Update was cancelled.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to apply update: {ex.Message}");
            }
        }

        private static void CopyDirectoryWithRename(string sourceDir, string targetDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            Directory.CreateDirectory(targetDir);

            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDir, file.FullName);
                string destFile = Path.Combine(targetDir, relativePath);
                string? destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                try
                {
                    file.CopyTo(destFile, overwrite: true);
                }
                catch (IOException)
                {
                    // File is locked by the currently running process.
                    // Windows allows renaming open/running files. Rename to .old and copy the fresh version.
                    string oldFile = destFile + ".old";
                    try { if (File.Exists(oldFile)) File.Delete(oldFile); } catch { }
                    try { File.Move(destFile, oldFile); } catch { }
                    file.CopyTo(destFile, overwrite: true);
                }
            }
        }

        public static void CleanUpOldFiles(string targetDir)
        {
            try
            {
                var dir = new DirectoryInfo(targetDir);
                if (!dir.Exists) return;

                foreach (var file in dir.GetFiles("*.old", SearchOption.AllDirectories))
                {
                    try { file.Delete(); } catch { }
                }
            }
            catch { }
        }

        public static bool CompareVersions(string remoteVersionStr, string currentVersionStr)
        {
            if (string.IsNullOrWhiteSpace(remoteVersionStr)) return false;

            // Normalize "2.0" to "2.0.0"
            string r = remoteVersionStr.Trim().TrimStart('v', 'V');
            string c = currentVersionStr.Trim().TrimStart('v', 'V');

            // Try standard System.Version parsing
            if (Version.TryParse(r, out var rVer) && Version.TryParse(c, out var cVer))
            {
                return rVer > cVer;
            }

            // Fallback split by dots for non-standard semver (e.g. "2.0.0-beta")
            var rParts = r.Split('-', '+')[0].Split('.');
            var cParts = c.Split('-', '+')[0].Split('.');

            int maxLen = Math.Max(rParts.Length, cParts.Length);
            for (int i = 0; i < maxLen; i++)
            {
                int rNum = (i < rParts.Length && int.TryParse(rParts[i], out int rp)) ? rp : 0;
                int cNum = (i < cParts.Length && int.TryParse(cParts[i], out int cp)) ? cp : 0;
                if (rNum > cNum) return true;
                if (rNum < cNum) return false;
            }

            return false;
        }

        private class GitHubReleaseDto
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }

            [JsonPropertyName("assets")]
            public GitHubAssetDto[]? Assets { get; set; }
        }

        private class GitHubAssetDto
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;

            [JsonPropertyName("size")]
            public long Size { get; set; }
        }
    }
}
