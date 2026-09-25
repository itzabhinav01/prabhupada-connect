using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Credential storage implementation using Windows DPAPI where available,
    /// with an isolated encrypted store under %LOCALAPPDATA%\VedaBaseModern.
    /// </summary>
    public class WindowsCredentialStorageService : ICredentialStorageService
    {
        private readonly string _storagePath;
        private readonly ConcurrentDictionary<string, string> _memoryFallback = new();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VedaBaseModern_ResearchSync_Entropy");

        public WindowsCredentialStorageService(string? storagePath = null)
        {
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appDir = Path.Combine(localAppData, "VedaBaseModern");
                Directory.CreateDirectory(appDir);
                _storagePath = Path.Combine(appDir, "sync_credentials.bin");
            }
            else
            {
                _storagePath = storagePath;
                string? dir = Path.GetDirectoryName(storagePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
        }

        public async Task SaveCredentialsAsync(string key, string secret)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(secret);

            _memoryFallback[key] = secret;

            await Task.Run(() =>
            {
                try
                {
                    var store = ReadStoreInternal();
                    store[key] = secret;
                    WriteStoreInternal(store);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Credentials] Failed to persist encrypted credentials: {ex.Message}");
                }
            });
        }

        public async Task<string?> GetCredentialsAsync(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            if (_memoryFallback.TryGetValue(key, out var cached))
            {
                return cached;
            }

            return await Task.Run(() =>
            {
                try
                {
                    var store = ReadStoreInternal();
                    if (store.TryGetValue(key, out var val))
                    {
                        _memoryFallback[key] = val;
                        return val;
                    }
                    return null;
                }
                catch
                {
                    return null;
                }
            });
        }

        public async Task DeleteCredentialsAsync(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            _memoryFallback.TryRemove(key, out _);

            await Task.Run(() =>
            {
                try
                {
                    var store = ReadStoreInternal();
                    if (store.TryRemove(key, out _))
                    {
                        WriteStoreInternal(store);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Credentials] Failed to delete credential: {ex.Message}");
                }
            });
        }

        private ConcurrentDictionary<string, string> ReadStoreInternal()
        {
            if (!File.Exists(_storagePath))
            {
                return new ConcurrentDictionary<string, string>();
            }

            try
            {
                byte[] fileBytes = File.ReadAllBytes(_storagePath);
                if (fileBytes.Length == 0) return new ConcurrentDictionary<string, string>();

                byte[] plainBytes;
                if (OperatingSystem.IsWindows())
                {
                    plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(fileBytes, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                }
                else
                {
                    plainBytes = fileBytes;
                }

                string json = Encoding.UTF8.GetString(plainBytes);
                var dict = JsonSerializer.Deserialize<ConcurrentDictionary<string, string>>(json);
                return dict ?? new ConcurrentDictionary<string, string>();
            }
            catch
            {
                return new ConcurrentDictionary<string, string>();
            }
        }

        private void WriteStoreInternal(ConcurrentDictionary<string, string> store)
        {
            string json = JsonSerializer.Serialize(store);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);

            byte[] cipherBytes;
            if (OperatingSystem.IsWindows())
            {
                cipherBytes = System.Security.Cryptography.ProtectedData.Protect(plainBytes, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            }
            else
            {
                cipherBytes = plainBytes;
            }

            File.WriteAllBytes(_storagePath, cipherBytes);
        }
    }
}
