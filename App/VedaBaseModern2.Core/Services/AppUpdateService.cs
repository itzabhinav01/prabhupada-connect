using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        public string StatusMessage { get; set; } = string.Empty;
        public bool CheckSucceeded { get; set; }
    }

    public interface IAppUpdateService
    {
        Task<AppUpdateInfo> CheckForUpdatesAsync(string? currentVersionOverride = null);
    }

    /// <summary>
    /// Checks the official GitHub repository for updates and release installers.
    /// Safely handles offline states, rate limits, and non-existing releases.
    /// </summary>
    public class AppUpdateService : IAppUpdateService
    {
        public const string GitHubApiUrl = "https://api.github.com/repos/itzabhinav01/prabhupada-connect/releases/latest";
        public const string FallbackReleaseUrl = "https://github.com/itzabhinav01/prabhupada-connect/releases";
        private readonly HttpClient _httpClient;

        public AppUpdateService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
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

                // Look for direct executable installer in release assets (.exe)
                var exeAsset = release.Assets?.FirstOrDefault(a => 
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    a.BrowserDownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                info.DownloadUrl = exeAsset?.BrowserDownloadUrl ?? info.ReleaseUrl;

                bool isNewer = CompareVersions(cleanTag, currentVersion);
                info.CheckSucceeded = true;
                info.IsUpdateAvailable = isNewer;

                if (isNewer)
                {
                    info.StatusMessage = $"Update available: v{cleanTag} is ready for download!";
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
        }
    }
}
