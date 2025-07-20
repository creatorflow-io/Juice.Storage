using System.Security.Claims;
using Juice.Storage.Abstractions;
using Juice.Storage.Authorization;
using Juice.Storage.Dto;
using Juice.Storage.Events;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Juice.Storage
{
    internal class DefaultUploadManager<TFile> : IUploadManager
        where TFile : class, IFile, new()
    {
        public IStorage Storage => _storage;
        private IStorageResolver _storageResolver;
        private IStorage _storage;
        private IUploadRepository<TFile> _uploadRepository;
        private IFileRepository<TFile>? _fileRepository;
        private IFileNameGenerator<TFile>? _fileNameGenerator;
        private IQuotaChecker<TFile>? _quotaChecker;

        private IAuthorizationService? _authorizationService;
        private IHttpContextAccessor? _httpContextAccessor;

        private IOptionsSnapshot<UploadOptions> _options;

        private IMediator? _mediator;

        private ILogger _logger;
        public DefaultUploadManager(
            IStorageResolver storageResolver,
            IStorage storage,
            IUploadRepository<TFile> uploadRepository,
            IOptionsSnapshot<UploadOptions> options,
            ILogger<DefaultUploadManager<TFile>> logger,
            IHttpContextAccessor? httpContextAccessor = default,
            IFileRepository<TFile>? fileRepository = default,
            IFileNameGenerator<TFile>? fileNameGenerator = default,
            IAuthorizationService? authorizationService = default,
            IQuotaChecker<TFile>? quotaService = default,
            IMediator? mediator = default)
        {
            _storageResolver = storageResolver;
            _storage = storage;
            _uploadRepository = uploadRepository;
            _options = options;
            _fileRepository = fileRepository;
            _fileNameGenerator = fileNameGenerator;
            _quotaChecker = quotaService;
            _authorizationService = authorizationService;
            _httpContextAccessor = httpContextAccessor;
            if(authorizationService !=null && httpContextAccessor == null)
            {
                throw new ArgumentNullException(nameof(httpContextAccessor));
            }
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<bool> CompleteAsync(Guid uploadId, CancellationToken token)
        {
            var preserved = false;
            if (await _uploadRepository.ExistsAsync(_storageResolver.Identity, uploadId, token))
            {
                var file = await _uploadRepository.GetAsync(_storageResolver.Identity, uploadId, token);

                if (file.LastModified.HasValue && _options.Value.PreserveDateModified)
                {
                    try
                    {
                        await _storage.PreserveModifiedTimeAsync(file.Name, file.LastModified, token);
                        preserved = true;
                        file.DateModifiedPreserved = true;
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (_fileRepository != null)
                {
                    await _fileRepository.AddAsync(file, _storageResolver.Identity, token);
                }
                if (_mediator != null)
                {
                    var username = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;
                    await _mediator.Publish(new FileUploadedEvent(file.Id, file.Name, file.ContentType, file.PackageSize, file.CorrelationId, file.Metadata, username), token);
                }
                await _uploadRepository.CompleteAsync(_storageResolver.Identity, uploadId, token);
            }
            return preserved;
        }

        public async Task FailureAsync(Guid uploadId, CancellationToken token)
        {
            if (await _uploadRepository.ExistsAsync(_storageResolver.Identity, uploadId, token))
            {
                var file = await _uploadRepository.GetAsync(_storageResolver.Identity, uploadId, token);

                await _storage.DeleteAsync(file.Name, token);

                await _uploadRepository.RemoveAsync(_storageResolver.Identity, uploadId, token);

                if (_mediator != null)
                {
                    var username = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;
                    await _mediator.Publish(new FileUploadFailedEvent(file.Name, file.CorrelationId, username), token);
                }
            }
            else
            {
                _logger.LogInformation("UploadId {UploadId} could not be found.", uploadId);
            }
        }

        public async Task TimedoutAsync(Guid uploadId, CancellationToken token)
        {
            if (await _uploadRepository.ExistsAsync(_storageResolver.Identity, uploadId, token))
            {
                var file = await _uploadRepository.GetAsync(_storageResolver.Identity, uploadId, token);
                await _storage.DeleteAsync(file.Name, token);
                await _uploadRepository.RemoveAsync(_storageResolver.Identity, uploadId, token);
            }
            else
            {
                _logger.LogInformation("UploadId {UploadId} could not be found.", uploadId);
            }
        }

        public Task<bool> ExistsAsync(string filePath, CancellationToken token)
        {
            return _storage.ExistsAsync(filePath, token);
        }

        public async Task<UploadConfiguration> InitAsync(InitialFileInfo fileInfo, CancellationToken token)
        {
            var serverRequestBodyLimit = UploadOptions.ServerMaxBodySizeFromHandledError ?? _httpContextAccessor?.HttpContext?.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
            var sectionSize = Math.Min(_options.Value.SectionSize, serverRequestBodyLimit ?? long.MaxValue);

            if (fileInfo.FileExistsBehavior == FileExistsBehavior.Resume)
            {
                if (!fileInfo.UploadId.HasValue)
                {
                    throw new ArgumentException("UploadId must has value to resume upload process.");
                }
                if (!await _uploadRepository.ExistsAsync(_storageResolver.Identity, fileInfo.UploadId.Value, token))
                {
                    throw new ArgumentException("UploadId could not be found.");
                }

                var file = await _uploadRepository.GetAsync(_storageResolver.Identity, fileInfo.UploadId.Value, token);
                var fileName = file.Name;

                var exists = await _storage.ExistsAsync(fileName, token);
                if (!exists)
                {
                    throw new ArgumentException($"Uploading file {fileName} no longer exists.");
                }
                var size = await _storage.FileSizeAsync(fileName, token);

                if (_quotaChecker != null
                    && await _quotaChecker.IsQuotaLimitExceededAsync(_httpContextAccessor?.HttpContext?.User, file, size))
                {
                    throw new InvalidOperationException("Quota limit exceeded.");
                }

                if (_mediator != null)
                {
                    var username = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;
                    await _mediator.Publish(new FileUploadResumedEvent(file.Id, fileName, size, file.CorrelationId, username), token);
                }

                return new UploadConfiguration(fileInfo.UploadId.Value, fileName, sectionSize, true, file.PackageSize, size);
            }
            else
            {
                var id = Guid.NewGuid();
                var file = new TFile()
                {
                    Id = id,
                    Name = fileInfo.Name,
                    PackageSize = fileInfo.FileSize,
                    ContentType = fileInfo.ContentType,
                    CorrelationId = fileInfo.CorrelationId,
                    Metadata = fileInfo.Metadata,
                    OriginalName = fileInfo.OriginalName,
                    LastModified = fileInfo.LastModified
                };

                if (_fileNameGenerator != null)
                {
                    file.Name = await _fileNameGenerator.GenerateAsync(file, token);
                }

                if (_authorizationService != null)
                {
                    var authorizationResult = await _authorizationService.AuthorizeAsync(_httpContextAccessor!.HttpContext!.User, file, StoragePolicies.CreateFile);
                    if (!authorizationResult.Succeeded)
                    {
                        throw new UnauthorizedAccessException("You are unauthorized to upload this file.");
                    }
                }
                if (_quotaChecker != null
                   && await _quotaChecker.IsQuotaLimitExceededAsync(_httpContextAccessor?.HttpContext?.User, file))
                {
                    throw new InvalidOperationException("Quota limit exceeded.");
                }
                var createdFileName = default(string);
                try
                {
                    createdFileName = await _storage.CreateAsync(fileInfo.Name, new CreateFileOptions { FileExistsBehavior = fileInfo.FileExistsBehavior }, token);

                    file.Name = createdFileName;
                    await _uploadRepository.AddAsync(_storageResolver.Identity, file);

                    if (_mediator != null)
                    {
                        var username = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;
                        await _mediator.Publish(new FileUploadStartedEvent(file.Id, file.Name, file.ContentType, file.PackageSize, file.CorrelationId, file.Metadata, username), token);
                    }

                    return new UploadConfiguration(id, createdFileName, sectionSize, false, fileInfo.FileSize, 0);
                }
                catch (Exception)
                {
                    if (!string.IsNullOrEmpty(createdFileName))
                    {
                        await _storage.DeleteAsync(createdFileName, default);
                    }
                    throw;
                }
            }
        }

        public async Task<(bool Completed, bool DateModifiedPreserved, long Size)> UploadAsync(Guid uploadId, Stream stream, long offset, CancellationToken token)
        {
            if (!await _uploadRepository.ExistsAsync(_storageResolver.Identity, uploadId, token))
            {
                throw new ArgumentException("UploadId could not be found.");
            }

            var file = await _uploadRepository.GetAsync(_storageResolver.Identity, uploadId, token);
            var fileName = file.Name;

            try
            {
                await _storage.WriteAsync(fileName, stream, offset, new TransferOptions(), token);
            }
            catch (OperationCanceledException)
            {
                if (_options.Value.DeleteOnAbort)
                {
                    await _storage.DeleteAsync(fileName, default); // token is already cancelled
                }
                await _uploadRepository.AbortAsync(_storageResolver.Identity, uploadId, _options.Value.DeleteOnAbort);
                throw;
            }
            var fileSize = await _storage.FileSizeAsync(fileName, token);
            if (fileSize == file.PackageSize)
            {
                var preserved = await CompleteAsync(uploadId, token);
                return (true, preserved, fileSize);
            }
            return (false, false, fileSize);
        }
    }

}
