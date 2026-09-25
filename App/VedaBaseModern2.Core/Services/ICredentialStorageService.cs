using System.Threading.Tasks;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Service for securely persisting sensitive cloud provider credentials (Phase 5).
    /// Prevents saving plaintext API keys in unencrypted database tables.
    /// </summary>
    public interface ICredentialStorageService
    {
        Task SaveCredentialsAsync(string key, string secret);
        Task<string?> GetCredentialsAsync(string key);
        Task DeleteCredentialsAsync(string key);
    }
}
