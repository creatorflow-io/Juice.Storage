using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Juice.Storage.Abstractions;
using Juice.Storage.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Juice.Storage
{
    internal class DefaultDownloadManager<TFile> : IDownloadManager
         where TFile : class, IFile
    {
        private readonly IFileRepository<TFile> _fileRepository;
        private IStorage _storage;
        private IStorageResolver _storageResolver;
        private IAuthorizationService? _authorizationService;
        private IHttpContextAccessor _httpContextAccessor;

        public DefaultDownloadManager(IFileRepository<TFile> fileRepository, IStorage storage,
            IStorageResolver storageResolver,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationService? authorizationService = default)
        {
            _fileRepository = fileRepository;
            _storage = storage;
            _storageResolver = storageResolver;
            _httpContextAccessor = httpContextAccessor;
            _authorizationService = authorizationService;
        }
        public async Task<IOperationResult<(Stream Stream, string FileName)>> GetStreamAsync(Guid id, CancellationToken token)
        {
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
    }
}
