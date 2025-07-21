
using Juice.Storage.Abstractions;
using Juice.Storage.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Juice.Storage
{
    internal class DefaultDownloadManager<TFile> : IDownloadManager
         where TFile : class, IFile
    {
        private readonly IFileRepository<TFile>? _fileRepository;
        private IStorage _storage;
        private IStorageResolver _storageResolver;
        private IAuthorizationService? _authorizationService;
        private IHttpContextAccessor _httpContextAccessor;

        private readonly IOptionsSnapshot<DownloadOptions> _optionsSnapshot;

        public DefaultDownloadManager(IStorage storage,
            IStorageResolver storageResolver,
            IOptionsSnapshot<DownloadOptions> optionsSnapshot,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationService? authorizationService = default,
            IFileRepository<TFile>? fileRepository = default)
        {
            _fileRepository = fileRepository;
            _storage = storage;
            _storageResolver = storageResolver;
            _optionsSnapshot = optionsSnapshot;
            _httpContextAccessor = httpContextAccessor;
            _authorizationService = authorizationService;
        }
        public async Task<IOperationResult<(Stream Stream, string FileName)>> GetStreamAsync(Guid id, CancellationToken token)
        {
            if (_fileRepository == null)
            {
                return OperationResult.Failed<(Stream, string)>("File repository is not configured. Please use file path to download file.");
            }
            var file = await _fileRepository.GetAsync(_storageResolver.Identity, id, token);
            if (file == null)
            {
                return OperationResult.NotFound<(Stream, string)>(file, $"The file with id {id} not found.");
            }
            if (_authorizationService != null && _httpContextAccessor.HttpContext!=null)
            {
                var authorizationResult = await _authorizationService.AuthorizeAsync(_httpContextAccessor.HttpContext.User, file, StoragePolicies.DownloadFile);
                if (!authorizationResult.Succeeded)
                {
                    return OperationResult.Unauthorized("You are not authorized to download this file.").Of<(Stream, string)>();
                }
            }

            var exists = await _storage.ExistsAsync(file.Name, token);
            if (!exists)
            {
                return OperationResult.NotFound<(Stream, string)>(file, $"The file with id {id} is no longer exist in storage.");
            }
            try
            {
                var stream = await _storage.ReadAsync(file.Name, token);
                return OperationResult.Result((stream, file.Name));
            }
            catch (Exception ex)
            {
                return OperationResult.Failed<(Stream, string)>($"Error while reading file {file.Name} from storage: {ex.Message}");
            }
        }

        public async Task<IOperationResult<Stream>> GetStreamAsync(string filePath, CancellationToken token)
        {
            if (!_optionsSnapshot.Value.IsSupportDownloadByPath)
            {
                return OperationResult.Failed<Stream>("Download by path is not supported. Please use file id to download file.");
            }
            if (_authorizationService != null && _httpContextAccessor.HttpContext != null)
            {
                var authorizationResult = await _authorizationService.AuthorizeAsync(_httpContextAccessor.HttpContext.User, StoragePolicies.DownloadFile);
                if (!authorizationResult.Succeeded)
                {
                    return OperationResult.Unauthorized("You are not authorized to download file.").Of<Stream>();
                }
            }

            var exists = await _storage.ExistsAsync(filePath, token);
            if (!exists)
            {
                return OperationResult.NotFound<Stream>(filePath, $"The file {filePath} is no longer exist in storage.");
            }

            try
            {
                var stream = await _storage.ReadAsync(filePath, token);
                return OperationResult.Result(stream);
            }
            catch (Exception ex)
            {
                return OperationResult.Failed<Stream>($"Error while reading file {filePath} from storage: {ex.Message}");
            }
        }
    }
}
