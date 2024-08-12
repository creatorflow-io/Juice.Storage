namespace Juice.Storage
{
    public interface IFileRepository<T>
        where T : class, IFile
    {
        Task AddAsync(T item, string storageIdentity, CancellationToken token);
    }
}
