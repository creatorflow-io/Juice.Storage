using System.Collections.Concurrent;
using Juice.Storage.InMemory;

namespace Juice.Storage.App
{
    internal class FileInfo
    {
        public Guid Id { get; init; }
        public string StorageIdentity { get; init; }
        public UploadFileInfo File { get; init; }
        public FileInfo(Guid id, string storageIdentity, UploadFileInfo file)
        {
            Id = id;
            StorageIdentity = storageIdentity;
            File = file;
        }
    }

    internal class InMemoryFileRepository : IFileRepository<UploadFileInfo>
    {
        private readonly ConcurrentBag<FileInfo> _files = new();
        private ILogger _logger;

        public InMemoryFileRepository(ILogger<InMemoryFileRepository> logger)
        {
            _logger = logger;
        }

        public Task AddAsync(UploadFileInfo item, string storageIdentity, CancellationToken token)
        {
            if (_files.Any(f => f.Id == item.Id && f.StorageIdentity == storageIdentity))
            {
                throw new InvalidOperationException("File with the same ID and storage identity already exists.");
            }
            _files.Add(new FileInfo(item.Id, storageIdentity, item));
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("File with ID {Id} added to storage {StorageIdentity}.", item.Id, storageIdentity);
            }
            return Task.CompletedTask;
        }
        public Task<UploadFileInfo?> GetAsync(string storageIdentity, Guid id, CancellationToken token)
        {
            var file = _files.FirstOrDefault(f => f.StorageIdentity == storageIdentity && f.Id == id);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogInformation("Retrieving file with ID {Id} from storage {StorageIdentity}. {found}", id, storageIdentity, file != null);
            }
            return Task.FromResult(file?.File);
        }
    }
}
