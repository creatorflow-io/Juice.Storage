using System.Net;
using System.Text.RegularExpressions;

namespace Juice.Storage.Abstractions
{
    public abstract class StorageProviderBase : IStorageProvider
    {
        protected string _copyNumberPattern = @"(?<n>[^\n]+)\((?<cn>[0-9]+)\)[\s]*\.[\S]+$";
        private static readonly char[] _pathSeparators = new[] { '/', '\\' };
        public NetworkCredential? Credential { get; protected set; }
        public StorageEndpoint? StorageEndpoint { get; protected set; }
        public virtual int Priority { get; protected set; }

        public abstract Protocol[] Protocols { get; }

        protected virtual void CheckEndpoint()
        {
            if (string.IsNullOrWhiteSpace(StorageEndpoint?.Uri))
            {
                throw new ArgumentException("StorageEndpoint uri must be configured.");
            }
        }

        public virtual IStorageProvider WithCredential(NetworkCredential credential)
        {
            Credential = credential;
            return this;
        }

        public virtual IStorageProvider Configure(StorageEndpoint endpoint, int? priority = default)
        {
            StorageEndpoint = endpoint;

            if (priority.HasValue)
            {
                Priority = priority.Value;
            }

            if (!string.IsNullOrWhiteSpace(endpoint.Identity))
            {
                return this.WithCredential(new NetworkCredential(endpoint.Identity, endpoint.Password));
            }
            return this;
        }
        /// <summary>
        /// List file that has same directory, name and extension with specified file but ends with copy number
        /// Ex: abc(1).xyz
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        protected abstract Task<IList<string>> FindFileVersionsAsync(string filePath, CancellationToken token);

        /// <summary>
        /// Match file's copy number, origin file name without extension and copy number
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        protected virtual (int? CopyNumber, string OriginName) MatchCopyNumber(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var nameRegex = new Regex(_copyNumberPattern, RegexOptions.IgnoreCase);
            Match match = nameRegex.Match(fileName);
            if (match.Success)
            {
                var copyNumberString = match.Groups["cn"].Value;
                if (int.TryParse(copyNumberString, out var ver))
                {
                    return (ver, match.Groups["n"].Value);
                }
                // replace original file path to search with file name without version (abc.xyz instead abc(1).xyz)
            }
            return (null, Path.GetFileNameWithoutExtension(fileName));
        }

        /// <summary>
        /// Normalize a file path relative to the storage endpoint to the platform independent form "dir/sub/file.ext".
        /// <para>Both '/' and '\' are accepted as separators; leading separators are removed.</para>
        /// </summary>
        /// <exception cref="ArgumentException">The path is empty, contains '..' segments or a drive letter.</exception>
        protected virtual string NormalizePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path must not be empty.", nameof(filePath));
            }
            var segments = new List<string>();
            foreach (var segment in filePath.Split(_pathSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                {
                    continue;
                }
                // "..", "...", ".. " are all resolved to a parent directory on some platforms
                if (segment.Trim(' ', '.').Length == 0)
                {
                    throw new ArgumentException($"File path '{filePath}' must not contain parent directory segments.", nameof(filePath));
                }
                segments.Add(segment);
            }
            if (segments.Count == 0)
            {
                throw new ArgumentException("File path must not be empty.", nameof(filePath));
            }
            var first = segments[0];
            if (first.Length >= 2 && first[1] == ':' && char.IsLetter(first[0]))
            {
                throw new ArgumentException($"File path '{filePath}' must be relative to the storage endpoint.", nameof(filePath));
            }
            return string.Join("/", segments);
        }

        /// <summary>
        /// Directory part of a normalized path ("dir/sub" for "dir/sub/file.ext"), or empty string.
        /// </summary>
        protected static string GetDirectoryPart(string normalizedPath)
        {
            var index = normalizedPath.LastIndexOf('/');
            return index > 0 ? normalizedPath.Substring(0, index) : "";
        }

        protected virtual async Task<string> GetNameAscendedCopyNumberAsync(string filePath, int? length, CancellationToken token)
        {
            filePath = NormalizePath(filePath);
            var directory = GetDirectoryPart(filePath);
            directory = !string.IsNullOrEmpty(directory) ? directory + "/" : "";

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            var searchPath = filePath;
            var copyNumber = 0;

            {
                // Check if specified file path is a copied version of an other. Ex: abc(1).xyz
                var (ver, origin) = MatchCopyNumber(filePath);
                if (ver.HasValue)
                {
                    copyNumber = ver.Value;
                    searchPath = directory + origin + extension;
                    fileNameWithoutExtension = origin;
                }
            }

            var fileVersions = await FindFileVersionsAsync(searchPath, token);

            foreach (var version in fileVersions)
            {
                // Take the highest copy number of the same origin name. Ex: abc(1).xyz, abc(3).xyz but not abcdef(5).xyz
                var (ver, origin) = MatchCopyNumber(version);
                if (ver.HasValue && ver.Value > copyNumber
                    && string.Equals(origin, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetExtension(version), extension, StringComparison.OrdinalIgnoreCase))
                {
                    copyNumber = ver.Value;
                }
            }

            copyNumber++;

            if (length.HasValue)
            {
                // calc length
                var additionNameLength = extension.Length + copyNumber.ToString().Length + 2;
                if (fileNameWithoutExtension.Length + additionNameLength > length)
                {
                    var strLen = length.Value - additionNameLength;
                    strLen = strLen > 0 ? strLen : 0;
                    fileNameWithoutExtension = fileNameWithoutExtension.Substring(0, strLen);
                }
            }
            var versionedPath = $"{directory}{fileNameWithoutExtension}({copyNumber}){extension}";

            while (await ExistsAsync(versionedPath, default))
            {
                versionedPath = $"{directory}{fileNameWithoutExtension}({++copyNumber}){extension}";
            }
            return versionedPath;
        }

        public abstract Task<string> CreateAsync(string filePath, CreateFileOptions options, CancellationToken token);
        public abstract Task<bool> ExistsAsync(string filePath, CancellationToken token);
        public abstract Task<long> FileSizeAsync(string filePath, CancellationToken token);
        public abstract Task<Stream> ReadAsync(string filePath, CancellationToken token);
        public abstract Task WriteAsync(string filePath, Stream stream, long offset, TransferOptions options, CancellationToken token);
        public abstract Task DeleteAsync(string filePath, CancellationToken token);
        public abstract Task PreserveModifiedTimeAsync(string filePath, DateTimeOffset? modifiedTime, CancellationToken token);

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //  dispose managed state (managed objects).

                }
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
