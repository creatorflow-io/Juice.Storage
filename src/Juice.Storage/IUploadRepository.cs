namespace Juice.Storage
{
    public interface IUploadRepository<T>
        where T : class, IFile, new()
    {
        Task<bool> ExistsAsync(string storageIdentity, Guid uploadId, CancellationToken token);
        Task<T> GetAsync(string storageIdentity, Guid uploadId, CancellationToken token);
        Task RemoveAsync(string storageIdentity, Guid uploadId, CancellationToken token);
        Task AddAsync(string storageIdentity, T item);
        Task AbortAsync(string storageIdentity, Guid uploadId, bool fileDeleted);
        Task CompleteAsync(string storageIdentity, Guid uploadId, CancellationToken token);
        Task<IEnumerable<T>> FindAllForCleanupAsync(string storageIdentity, DateTimeOffset beforeDate, CancellationToken token);
    }
}
