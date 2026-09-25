using System;
using System.Threading.Tasks;
using VedaBaseModern.Core.Repositories;

namespace VedaBaseModern.Core.Services
{
    public interface ISyncMetadataService
    {
        Task<string> GetDeviceIdAsync();
        Task<string?> GetValueAsync(string key);
        Task SetValueAsync(string key, string value);
    }

    public class SyncMetadataService : ISyncMetadataService
    {
        private readonly IUserRepository _userRepository;

        public SyncMetadataService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public Task<string> GetDeviceIdAsync() => _userRepository.GetDeviceIdAsync();

        public Task<string?> GetValueAsync(string key) => _userRepository.GetSyncMetadataAsync(key);

        public Task SetValueAsync(string key, string value) => _userRepository.SetSyncMetadataAsync(key, value);
    }
}
