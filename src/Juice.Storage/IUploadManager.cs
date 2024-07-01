using Juice.Storage.Abstractions;
using Juice.Storage.Dto;

namespace Juice.Storage
{
    public interface IUploadManager
    {
        IStorage Storage { get; }
        Task<bool> ExistsAsync(string filePath, CancellationToken token);
        Task<UploadConfiguration> InitAsync(InitialFileInfo fileInfo, CancellationToken token);
        /// <summary>
        /// Upload a chunk of data to the storage
        /// </summary>
        /// <param name="uploadId"></param>
        /// <param name="stream"></param>
        /// <param name="offset"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<(bool Completed, bool DateModifiedPreserved, long Size)> UploadAsync(Guid uploadId, Stream stream, long offset, CancellationToken token);
        /// <summary>
        /// Report the upload as completed, return DateModifiedPreserved
        /// </summary>
        /// <param name="uploadId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task<bool> CompleteAsync(Guid uploadId, CancellationToken token);
        Task FailureAsync(Guid uploadId, CancellationToken token);
        Task TimedoutAsync(Guid uploadId, CancellationToken token);
    }
}
