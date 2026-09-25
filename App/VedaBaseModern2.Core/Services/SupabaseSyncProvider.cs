using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VedaBaseModern.Core.Models;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Sync provider implementation for user-owned Supabase projects via PostgREST (Phase 5).
    /// Uses native HttpClient with zero external vendor SDK dependencies.
    /// </summary>
    public class SupabaseSyncProvider : ISyncProvider
    {
        private readonly HttpClient _httpClient;
        private readonly SupabaseConfig _config;
        private readonly string _deviceId;
        private readonly string _baseUrl;
        private readonly string _authUrl;
        private string? _authToken;
        private string? _userId;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public string ProviderId => "Supabase";

        public SupabaseSyncProvider(HttpClient httpClient, SupabaseConfig config, string deviceId)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _deviceId = deviceId ?? string.Empty;

            string rawUrl = config.ProjectUrl?.Trim() ?? string.Empty;
            if (rawUrl.EndsWith("/")) rawUrl = rawUrl[..^1];

            string rootUrl = rawUrl;
            if (rootUrl.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase))
            {
                rootUrl = rootUrl[..^"/rest/v1".Length];
            }
            if (rootUrl.EndsWith("/")) rootUrl = rootUrl[..^1];

            _baseUrl = $"{rootUrl}/rest/v1";
            _authUrl = $"{rootUrl}/auth/v1";
            _authToken = config.AuthToken;
            _userId = config.UserId;
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, $"{_baseUrl}/{path}");
            request.Headers.Add("apikey", _config.AnonKey);
            string token = !string.IsNullOrEmpty(_authToken)
                ? _authToken
                : (!string.IsNullOrEmpty(_config.AuthToken) ? _config.AuthToken : _config.AnonKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        public async Task<bool> AuthenticateAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.UserEmail) || string.IsNullOrWhiteSpace(_config.UserPassword))
            {
                return false;
            }

            try
            {
                var authBody = new
                {
                    email = _config.UserEmail,
                    password = _config.UserPassword
                };
                string json = JsonSerializer.Serialize(authBody);
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{_authUrl}/token?grant_type=password");
                req.Headers.Add("apikey", _config.AnonKey);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await _httpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    string respJson = await resp.Content.ReadAsStringAsync();
                    var authResp = JsonSerializer.Deserialize<SupabaseAuthResponseDto>(respJson, JsonOptions);
                    if (authResp != null && !string.IsNullOrEmpty(authResp.AccessToken))
                    {
                        _authToken = authResp.AccessToken;
                        _config.AuthToken = authResp.AccessToken;
                        if (authResp.User != null && !string.IsNullOrEmpty(authResp.User.Id))
                        {
                            _userId = authResp.User.Id;
                            _config.UserId = authResp.User.Id;
                        }
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Supabase authentication failed: {ex.Message}");
                return false;
            }
        }

        public async Task<SyncConnectionTestResult> TestConnectionAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.ProjectUrl) || string.IsNullOrWhiteSpace(_config.AnonKey))
            {
                return new SyncConnectionTestResult
                {
                    Success = false,
                    ErrorMessage = "Project URL and API Key must be configured."
                };
            }

            try
            {
                // If user credentials are provided, authenticate first
                if (!string.IsNullOrEmpty(_config.UserEmail) && !string.IsNullOrEmpty(_config.UserPassword))
                {
                    bool authOk = await AuthenticateAsync();
                    if (!authOk)
                    {
                        return new SyncConnectionTestResult
                        {
                            Success = false,
                            ErrorMessage = "Authentication failed: invalid email or password."
                        };
                    }
                }

                using var request = CreateRequest(HttpMethod.Get, "vb_schema_info?select=version&limit=1");
                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return new SyncConnectionTestResult
                    {
                        Success = true,
                        TablesExist = true
                    };
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return new SyncConnectionTestResult
                    {
                        Success = false,
                        ErrorMessage = "Authentication failed: invalid anon/public API key or token (HTTP 401)."
                    };
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return new SyncConnectionTestResult
                    {
                        Success = false,
                        ErrorMessage = "Access forbidden: Check Row Level Security policies (HTTP 403)."
                    };
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return new SyncConnectionTestResult
                    {
                        Success = false,
                        ErrorMessage = "Supabase rate limit exceeded (HTTP 429). Please wait before connecting."
                    };
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new SyncConnectionTestResult
                    {
                        Success = true,
                        TablesExist = false,
                        MissingTables = new List<string> { "vb_schema_info", "vb_bookmark_collections", "vb_bookmarks", "vb_highlights", "vb_notes" },
                        ErrorMessage = "Connected to Supabase project, but sync tables are not yet initialized. Please run supabase_schema.sql."
                    };
                }

                return new SyncConnectionTestResult
                {
                    Success = false,
                    ErrorMessage = $"Connection returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
                };
            }
            catch (Exception ex)
            {
                return new SyncConnectionTestResult
                {
                    Success = false,
                    ErrorMessage = $"Could not connect to Supabase: {ex.Message}"
                };
            }
        }

        public async Task<SyncRemoteMetadata> GetRemoteMetadataAsync()
        {
            var metadata = new SyncRemoteMetadata();
            try
            {
                using var request = CreateRequest(HttpMethod.Get, "vb_schema_info?select=version&limit=1");
                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var list = JsonSerializer.Deserialize<List<SupabaseSchemaInfoDto>>(json, JsonOptions);
                    if (list != null && list.Count > 0)
                    {
                        metadata.SchemaVersion = list[0].Version;
                        metadata.IsCompatible = metadata.SchemaVersion <= 1;
                    }
                }
            }
            catch
            {
                metadata.IsCompatible = true;
            }
            return metadata;
        }

        // Phase 7: a bounded lookback applied to the incremental pull
        // watermark only (never to what gets pushed, and never to conflict
        // resolution itself - LWW is unchanged). Fixes a confirmed gap: the
        // Supabase schema's `updated_at` column only DEFAULTs to NOW() on
        // INSERT, and SupabaseSyncProvider always supplies an explicit value
        // when pushing (the record's own local UpdatedUtc) - so a record
        // that Device B created before Device A's checkpoint last advanced,
        // but only pushed afterward, would otherwise never satisfy a strict
        // "updated_at > checkpoint" filter on Device A ever again. Re-pulling
        // a small window of already-seen records is harmless (MergeSyncChangesAsync
        // is idempotent); silently missing a record forever is not.
        private static readonly TimeSpan SyncLookbackBuffer = TimeSpan.FromHours(24);

        public async Task<RemoteChangeSet> PullChangesAsync(DateTime? sinceUtc)
        {
            var result = new RemoteChangeSet();
            DateTime? effectiveSince = sinceUtc?.Subtract(SyncLookbackBuffer);
            string queryFilter = effectiveSince.HasValue
                ? $"&updated_at=gt.{Uri.EscapeDataString(effectiveSince.Value.ToUniversalTime().ToString("O"))}"
                : "";

            // 1. Collections
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"vb_bookmark_collections?select=*&order=updated_at.asc{queryFilter}");
                using var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    ThrowHttpException(resp.StatusCode, err, "vb_bookmark_collections");
                }

                string json = await resp.Content.ReadAsStringAsync();
                var dtos = JsonSerializer.Deserialize<List<SupabaseCollectionDto>>(json, JsonOptions);
                if (dtos != null)
                {
                    foreach (var d in dtos)
                    {
                        result.Collections.Add(new BookmarkCollection
                        {
                            Id = d.Id,
                            Name = d.Name,
                            CreatedUtc = d.CreatedAt.ToUniversalTime(),
                            UpdatedUtc = d.UpdatedAt.ToUniversalTime(),
                            DeletedUtc = d.DeletedAt?.ToUniversalTime(),
                            SortOrder = d.SortOrder
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Pull collections failed: {ex.Message}");
                throw;
            }

            // 2. Bookmarks
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"vb_bookmarks?select=*&order=updated_at.asc{queryFilter}");
                using var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    ThrowHttpException(resp.StatusCode, err, "vb_bookmarks");
                }

                string json = await resp.Content.ReadAsStringAsync();
                var dtos = JsonSerializer.Deserialize<List<SupabaseBookmarkDto>>(json, JsonOptions);
                if (dtos != null)
                {
                    foreach (var d in dtos)
                    {
                        result.Bookmarks.Add(new UserBookmark
                        {
                            Id = d.Id,
                            RecordKey = d.RecordKey,
                            CollectionId = d.CollectionId,
                            Title = d.Title,
                            CreatedUtc = d.CreatedAt.ToUniversalTime(),
                            UpdatedUtc = d.UpdatedAt.ToUniversalTime(),
                            DeletedUtc = d.DeletedAt?.ToUniversalTime()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Pull bookmarks failed: {ex.Message}");
                throw;
            }

            // 3. Highlights
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"vb_highlights?select=*&order=updated_at.asc{queryFilter}");
                using var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    ThrowHttpException(resp.StatusCode, err, "vb_highlights");
                }

                string json = await resp.Content.ReadAsStringAsync();
                var dtos = JsonSerializer.Deserialize<List<SupabaseHighlightDto>>(json, JsonOptions);
                if (dtos != null)
                {
                    foreach (var d in dtos)
                    {
                        result.Highlights.Add(new Highlight
                        {
                            Id = d.Id,
                            RecordKey = d.RecordKey,
                            Field = d.Field,
                            Color = Enum.TryParse<HighlightColor>(d.Color, ignoreCase: true, out var c) ? c : HighlightColor.Yellow,
                            StartOffset = d.StartOffset,
                            Length = d.Length,
                            SelectedText = d.SelectedText,
                            CreatedUtc = d.CreatedAt.ToUniversalTime(),
                            UpdatedUtc = d.UpdatedAt.ToUniversalTime(),
                            DeletedUtc = d.DeletedAt?.ToUniversalTime()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Pull highlights failed: {ex.Message}");
                throw;
            }

            // 4. Notes
            try
            {
                using var req = CreateRequest(HttpMethod.Get, $"vb_notes?select=*&order=updated_at.asc{queryFilter}");
                using var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    ThrowHttpException(resp.StatusCode, err, "vb_notes");
                }

                string json = await resp.Content.ReadAsStringAsync();
                var dtos = JsonSerializer.Deserialize<List<SupabaseNoteDto>>(json, JsonOptions);
                if (dtos != null)
                {
                    foreach (var d in dtos)
                    {
                        result.Notes.Add(new UserNote
                        {
                            Id = d.Id,
                            RecordKey = d.RecordKey,
                            Title = d.Title,
                            Content = d.Content,
                            Field = d.Field,
                            StartOffset = d.StartOffset ?? -1,
                            Length = d.Length ?? -1,
                            CreatedUtc = d.CreatedAt.ToUniversalTime(),
                            UpdatedUtc = d.UpdatedAt.ToUniversalTime(),
                            DeletedUtc = d.DeletedAt?.ToUniversalTime()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Pull notes failed: {ex.Message}");
                throw;
            }

            return result;
        }

        public async Task<int> PushChangesAsync(LocalChangeSet changes)
        {
            if (changes == null || changes.TotalCount == 0) return 0;
            int pushed = 0;
            string? userId = _userId ?? _config.UserId;

            // 1. Collections
            if (changes.Collections.Count > 0)
            {
                var dtos = new List<SupabaseCollectionDto>();
                foreach (var c in changes.Collections)
                {
                    dtos.Add(new SupabaseCollectionDto
                    {
                        Id = c.Id,
                        UserId = userId,
                        Name = c.Name,
                        SortOrder = c.SortOrder,
                        CreatedAt = c.CreatedUtc.ToUniversalTime(),
                        UpdatedAt = c.UpdatedUtc.ToUniversalTime(),
                        DeletedAt = c.DeletedUtc?.ToUniversalTime(),
                        DeviceId = _deviceId
                    });
                }
                await UpsertBatchAsync("vb_bookmark_collections", dtos);
                pushed += dtos.Count;
            }

            // 2. Bookmarks
            if (changes.Bookmarks.Count > 0)
            {
                var dtos = new List<SupabaseBookmarkDto>();
                foreach (var b in changes.Bookmarks)
                {
                    dtos.Add(new SupabaseBookmarkDto
                    {
                        Id = b.Id,
                        UserId = userId,
                        RecordKey = b.RecordKey,
                        CollectionId = b.CollectionId,
                        Title = b.Title,
                        CreatedAt = b.CreatedUtc.ToUniversalTime(),
                        UpdatedAt = b.UpdatedUtc.ToUniversalTime(),
                        DeletedAt = b.DeletedUtc?.ToUniversalTime(),
                        DeviceId = _deviceId
                    });
                }
                await UpsertBatchAsync("vb_bookmarks", dtos);
                pushed += dtos.Count;
            }

            // 3. Highlights
            if (changes.Highlights.Count > 0)
            {
                var dtos = new List<SupabaseHighlightDto>();
                foreach (var h in changes.Highlights)
                {
                    dtos.Add(new SupabaseHighlightDto
                    {
                        Id = h.Id,
                        UserId = userId,
                        RecordKey = h.RecordKey,
                        Field = h.Field,
                        Color = h.Color.ToString(),
                        StartOffset = h.StartOffset,
                        Length = h.Length,
                        SelectedText = h.SelectedText,
                        CreatedAt = h.CreatedUtc.ToUniversalTime(),
                        UpdatedAt = h.UpdatedUtc.ToUniversalTime(),
                        DeletedAt = h.DeletedUtc?.ToUniversalTime(),
                        DeviceId = _deviceId
                    });
                }
                await UpsertBatchAsync("vb_highlights", dtos);
                pushed += dtos.Count;
            }

            // 4. Notes
            if (changes.Notes.Count > 0)
            {
                var dtos = new List<SupabaseNoteDto>();
                foreach (var n in changes.Notes)
                {
                    dtos.Add(new SupabaseNoteDto
                    {
                        Id = n.Id,
                        UserId = userId,
                        RecordKey = n.RecordKey,
                        Title = n.Title,
                        Content = n.Content,
                        Field = n.Field,
                        StartOffset = n.StartOffset >= 0 ? n.StartOffset : null,
                        Length = n.Length > 0 ? n.Length : null,
                        CreatedAt = n.CreatedUtc.ToUniversalTime(),
                        UpdatedAt = n.UpdatedUtc.ToUniversalTime(),
                        DeletedAt = n.DeletedUtc?.ToUniversalTime(),
                        DeviceId = _deviceId
                    });
                }
                await UpsertBatchAsync("vb_notes", dtos);
                pushed += dtos.Count;
            }

            return pushed;
        }

        private async Task UpsertBatchAsync<T>(string table, List<T> items)
        {
            if (items == null || items.Count == 0) return;

            string json = JsonSerializer.Serialize(items, JsonOptions);
            using var req = CreateRequest(HttpMethod.Post, table);
            req.Headers.Add("Prefer", "resolution=merge-duplicates");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync();
                ThrowHttpException(resp.StatusCode, err, table);
            }
        }

        private static void ThrowHttpException(System.Net.HttpStatusCode code, string responseBody, string table)
        {
            string msg = code switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Supabase authentication failed or session expired (HTTP 401).",
                System.Net.HttpStatusCode.Forbidden => $"Supabase access denied by Row Level Security on '{table}' (HTTP 403).",
                System.Net.HttpStatusCode.NotFound => $"Supabase table '{table}' not found (HTTP 404). Please verify your database schema.",
                System.Net.HttpStatusCode.TooManyRequests => "Supabase rate limit exceeded (HTTP 429). Please wait a moment before syncing again.",
                System.Net.HttpStatusCode.InternalServerError => "Supabase server error (HTTP 500). Please try again later.",
                _ => $"Supabase request to '{table}' failed with HTTP {(int)code}: {code}."
            };
            throw new HttpRequestException(msg, null, code);
        }

        public Task DisconnectAsync()
        {
            _authToken = null;
            _userId = null;
            return Task.CompletedTask;
        }
    }
}
