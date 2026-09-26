using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace Juice.Storage.InMemory
{
    internal class InMemoryUploadRepository<TFile> : IUploadRepository<TFile>
        where TFile : class, IFile
    {
        // accessed by concurrent requests: storage identity -> upload id -> file
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, TFile>> _uploads
            = new ConcurrentDictionary<string, ConcurrentDictionary<Guid, TFile>>();

        public Task AbortAsync(string storageIdentity, Guid uploadId, bool fileDeleted)
        {
            return Task.CompletedTask;
        }

        public Task CompleteAsync(string storageIdentity, Guid uploadId, CancellationToken token)
            => RemoveAsync(storageIdentity, uploadId, token);

        public Task AddAsync(string storageIdentity, TFile item)
        {
            if (storageIdentity == null) { throw new ArgumentNullException(nameof(storageIdentity)); }
            _uploads.GetOrAdd(storageIdentity, _ => new ConcurrentDictionary<Guid, TFile>())[item.Id] = item;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string storageIdentity, Guid uploadId, CancellationToken token)
        {
            if (storageIdentity == null) { throw new ArgumentNullException(nameof(storageIdentity)); }
            if (uploadId == Guid.Empty) { throw new ArgumentNullException(nameof(uploadId)); }
            return Task.FromResult(_uploads.TryGetValue(storageIdentity, out var uploads) && uploads.ContainsKey(uploadId));
        }

        public Task<IEnumerable<TFile>> FindAllForCleanupAsync(string storageIdentity, DateTimeOffset beforeDate, CancellationToken token)
        {
            if (storageIdentity == null) { throw new ArgumentNullException(nameof(storageIdentity)); }
            return Task.FromResult(_uploads.TryGetValue(storageIdentity, out var uploads)
                ? uploads.Values.Where(u => u.CreatedDate < beforeDate).ToArray().AsEnumerable()
                : Array.Empty<TFile>());
        }

        public Task<TFile> GetAsync(string storageIdentity, Guid uploadId, CancellationToken token)
        {
            if (storageIdentity == null) { throw new ArgumentNullException(nameof(storageIdentity)); }
            if (uploadId == Guid.Empty) { throw new ArgumentNullException(nameof(uploadId)); }
            if (_uploads.TryGetValue(storageIdentity, out var uploads) && uploads.TryGetValue(uploadId, out var file))
            {
                return Task.FromResult(file);
            }
            throw new KeyNotFoundException($"Upload {uploadId} not found in {storageIdentity}.");
        }

        public Task RemoveAsync(string storageIdentity, Guid uploadId, CancellationToken token)
        {
            if (storageIdentity == null) { throw new ArgumentNullException(nameof(storageIdentity)); }
            if (uploadId == Guid.Empty) { throw new ArgumentNullException(nameof(uploadId)); }
            if (_uploads.TryGetValue(storageIdentity, out var uploads))
            {
                uploads.TryRemove(uploadId, out _);
            }
            return Task.CompletedTask;
        }
    }

    public class UploadFileInfo : IFile
    {
        public Guid Id { get; init; }
        public string Name { get; set; }
        public long PackageSize { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public string? ContentType { get; set; }
        public string? OriginalName { get; init; }
        public string? CorrelationId { get; set; }
        public JObject? Metadata { get; set; }
        public DateTimeOffset CreatedDate { get; init; } = DateTimeOffset.Now;
        public bool DateModifiedPreserved { get; set; }
    }
}
