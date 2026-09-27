using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Configuration for anonymous app usage telemetry.
    /// Configure your Supabase Project URL and Public Anon Key below.
    /// </summary>
    public static class TelemetryConfig
    {
        /// <summary>
        /// Your Supabase Project URL (e.g., "https://abcdefghijklmnopqrst.supabase.co").
        /// Replace with your actual Supabase URL.
        /// </summary>
        public static string SupabaseUrl { get; set; } = "https://boqbdumzxgbzesbieewu.supabase.co";

        /// <summary>
        /// Your Supabase public 'anon' key (from Supabase Dashboard -> Project Settings -> API).
        /// Safe to include in client applications when Row Level Security (RLS) is enabled.
        /// </summary>
        public static string SupabaseAnonKey { get; set; } = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImJvcWJkdW16eGdiemVzYmllZXd1Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTA1MTUzNjUsImV4cCI6MjEwNjA5MTM2NX0.jmEAX6bHRv2hKWmboYoCBzTQsJdAa5hssobSAiqzEOA";

        /// <summary>
        /// Master switch to enable or disable telemetry pings.
        /// </summary>
        public static bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Minimum interval between heartbeat pings (default: 24 hours).
        /// Prevents spamming and keeps requests well within Supabase's free tier.
        /// </summary>
        public static readonly TimeSpan PingInterval = TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Lightweight, non-blocking telemetry service that sends an anonymous daily
    /// heartbeat ping to a free Supabase PostgreSQL backend.
    /// Tracks unique active installations using an anonymous, persistent Device GUID.
    /// Fails silently without impacting offline reading or startup performance.
    /// </summary>
    public class AppPingService
    {
        private readonly IUserRepository _userRepository;
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(6) };

        public AppPingService(IUserRepository userRepository)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        }

        /// <summary>
        /// Sends an anonymous heartbeat ping to Supabase if configured and throttled.
        /// </summary>
        /// <returns>True if a ping was sent successfully; false otherwise.</returns>
        public async Task<bool> SendHeartbeatPingAsync()
        {
            if (!TelemetryConfig.IsEnabled) return false;

            string url = TelemetryConfig.SupabaseUrl?.Trim() ?? string.Empty;
            string key = TelemetryConfig.SupabaseAnonKey?.Trim() ?? string.Empty;

            // Gracefully no-op if credentials are still placeholder values
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key) ||
                url.Contains("YOUR_PROJECT_ID", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("YOUR_SUPABASE_ANON_KEY", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                // Enforce 24-hour rate limiting across app restarts
                string? lastPingStr = await _userRepository.GetSyncMetadataAsync("LastTelemetryPingUtc");
                if (DateTime.TryParse(lastPingStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastPingUtc) &&
                    (DateTime.UtcNow - lastPingUtc) < TelemetryConfig.PingInterval)
                {
                    return false;
                }

                string deviceId = await _userRepository.GetDeviceIdAsync();
                if (string.IsNullOrWhiteSpace(deviceId)) return false;

                string rootUrl = url.TrimEnd('/');
                string endpoint = $"{rootUrl}/rest/v1/app_pings";

                string appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2.0.0";
                string osDesc = RuntimeInformation.OSDescription;

                var payload = new
                {
                    device_id = deviceId,
                    app_version = appVersion,
                    os_version = osDesc,
                    last_seen_at = DateTime.UtcNow.ToString("O")
                };

                string json = JsonSerializer.Serialize(payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("apikey", key);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Headers.Add("Prefer", "resolution=merge-duplicates");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    await _userRepository.SetSyncMetadataAsync("LastTelemetryPingUtc", DateTime.UtcNow.ToString("O"));
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Silently swallow network/timeout errors to preserve 100% offline capability
                System.Diagnostics.Debug.WriteLine($"[Telemetry] Silent heartbeat ping failed: {ex.Message}");
            }

            return false;
        }
    }
}
