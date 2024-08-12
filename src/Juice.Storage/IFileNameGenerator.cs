namespace Juice.Storage
{
    public interface IFileNameGenerator<T>
        where T : IFile
    {
        Task<string> GenerateAsync(T file, CancellationToken token);
    }
}
