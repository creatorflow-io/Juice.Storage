using System.Net;
using Juice.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Polly;

namespace Juice.Storage.Local
{
    public class LocalStorageProvider : StorageProviderBase
    {
        private ILogger _logger;

        private readonly INetworkConnectionFactory? _networkConnectionFactory;
        private INetworkConnection? _connection;
        private string? _rootPath;

        private readonly int _maxRetryCount = 3;
        private bool _bypassProvidedCredentials = false;

        public override Protocol[] Protocols => new Protocol[] { Protocol.Smb, Protocol.LocalDisk, Protocol.VirtualDirectory };

        public LocalStorageProvider(ILogger<LocalStorageProvider> logger,
            INetworkConnectionFactory? networkConnectionFactory = null)
        {
            _logger = logger;
            _networkConnectionFactory = networkConnectionFactory;
        }

        public override IStorageProvider Configure(StorageEndpoint endpoint, int? priority = default)
        {
            ResetConnection();
            return base.Configure(endpoint, priority);
        }

        public override IStorageProvider WithCredential(NetworkCredential credential)
        {
            base.WithCredential(credential);
            ResetConnection();
            return this;
        }

        private void ResetConnection()
        {
            _connection?.Dispose();
            _connection = null;
            _rootPath = null;
            _bypassProvidedCredentials = false;
        }

        /// <summary>
        /// Resolve the endpoint Uri to a path accessible through System.IO, connecting to the network share if needed.
        /// </summary>
        private string GetRootPath()
        {
            CheckEndpoint();
            if (_rootPath != null)
            {
                return _rootPath;
            }

            var uri = StorageEndpoint!.Uri;
            if (!UncPath.IsNetworkPath(uri))
            {
                return _rootPath = uri;
            }

            var hasCredential = !string.IsNullOrEmpty(Credential?.UserName);

            if (_networkConnectionFactory == null)
            {
                if (!OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException($"Storage endpoint '{uri}' is a network share that cannot be accessed directly on this platform. " +
                        $"Register a network connection factory (services.AddLinuxNetworkConnection()) or mount the share and configure its local path as the endpoint Uri.");
                }
                if (hasCredential)
                {
                    _logger.LogWarning("No {Factory} is registered, accessing {Path} with the process identity and ignoring the provided credentials. Register services.AddWindowsNetworkConnection() to use them.",
                        nameof(INetworkConnectionFactory), uri);
                }
                return _rootPath = UncPath.ToWindowsUnc(uri);
            }

            if (hasCredential && !_bypassProvidedCredentials)
            {
                try
                {
                    var policy = Policy.Handle<Exception>(ex => ex is not PlatformNotSupportedException)
                        .WaitAndRetry(2, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                        {
                            _logger.LogError(ex, "[Policy] Retrying init network connection to {Path} after {Timeout}s ({ExceptionMessage})", uri, $"{time.TotalSeconds:n1}", ex.Message);
                        });

                    _connection = policy.Execute(() => _networkConnectionFactory.Connect(StorageEndpoint, Credential!));
                    return _rootPath = _connection.ResolvePath(uri);
                }
                catch (Exception ex)
                {
                    // we only try to connect with provided credentials once
                    _bypassProvidedCredentials = true;
                    _logger.LogWarning(ex, "Error connecting to {Path} with provided credentials. Try default access with the process identity.", uri);
                }
            }

            return _rootPath = _networkConnectionFactory.ResolvePath(uri);
        }

        private string GetFullPath(string normalizedPath)
            => Path.Combine(GetRootPath(), normalizedPath.Replace('/', Path.DirectorySeparatorChar));

        public override async Task<string> CreateAsync(string filePath, CreateFileOptions options, CancellationToken token)
        {
            filePath = NormalizePath(filePath);
            var fullPath = GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(fullPath))
            {
                await File.Create(fullPath).DisposeAsync();
                return filePath;
            }
            var fileExistsBehavior = options?.FileExistsBehavior ?? FileExistsBehavior.RaiseError;
            switch (fileExistsBehavior)
            {
                case FileExistsBehavior.RaiseError:
                    throw new IOException("File is already exists.");
                case FileExistsBehavior.Replace:
                    File.Delete(fullPath);

                    await File.Create(fullPath).DisposeAsync();
                    return filePath;

                case FileExistsBehavior.AscendedCopyNumber:
                    var newPath = await GetNameAscendedCopyNumberAsync(filePath, default, token);
                    await File.Create(GetFullPath(newPath)).DisposeAsync();
                    return newPath;
                default: throw new IOException("File is already exists.");
            }

        }
        public override Task DeleteAsync(string filePath, CancellationToken token)
        {
            var fullPath = GetFullPath(NormalizePath(filePath));
            var policy = Policy.Handle<IOException>()
                .WaitAndRetry(_maxRetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                {
                    _logger.LogError(ex, "[Policy] Retrying delete file: {filePath} after {Timeout}s ({ExceptionMessage})", filePath, $"{time.TotalSeconds:n1}", ex.Message);
                });

            return policy.Execute(() =>
            {
                File.Delete(fullPath);
                return Task.CompletedTask;
            });

        }
        public override Task<bool> ExistsAsync(string filePath, CancellationToken token)
        {
            var fullPath = GetFullPath(NormalizePath(filePath));
            return Task.FromResult(File.Exists(fullPath));
        }
        public override Task<long> FileSizeAsync(string filePath, CancellationToken token)
        {
            var fullPath = GetFullPath(NormalizePath(filePath));

            var policy = Policy.Handle<IOException>()
                .WaitAndRetry(_maxRetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                {
                    _logger.LogError(ex, "[Policy] Retrying get file size: {filePath} after {Timeout}s ({ExceptionMessage})", filePath, $"{time.TotalSeconds:n1}", ex.Message);
                });

            return Task.FromResult(policy.Execute(() =>
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Get file size for {fullPath}", fullPath);
                }
                return new FileInfo(fullPath).Length;
            }));
        }

        public override Task<Stream> ReadAsync(string filePath, CancellationToken token)
        {
            var fullPath = GetFullPath(NormalizePath(filePath));
            return Task.FromResult<Stream>(File.OpenRead(fullPath));
        }

        public override async Task WriteAsync(string filePath, Stream stream, long offset, TransferOptions options, CancellationToken token)
        {
            var fullPath = GetFullPath(NormalizePath(filePath));
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Writing to {fullPath}, absolute path {absolute}", fullPath, Path.GetFullPath(fullPath));
            }
            if (offset > 0)
            {
                var size = await FileSizeAsync(filePath, token);
                if (offset != size)
                {
                    throw new Exception("File cannot be resume from position");
                }
            }

            var policy = Policy.Handle<IOException>()
                .WaitAndRetryAsync(_maxRetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (ex, time) =>
                {
                    _logger.LogError(ex, "[Policy] Retrying write file: {filePath} after {Timeout}s ({ExceptionMessage})", filePath, $"{time.TotalSeconds:n1}", ex.Message);
                });
            await policy.ExecuteAsync(async () =>
            {
                using var ostream = File.OpenWrite(fullPath);
                ostream.Seek(0L, SeekOrigin.End);
                try
                {
                    if (options.BufferSize.HasValue)
                    {
                        await stream.CopyToAsync(ostream, options.BufferSize.Value, token);
                    }
                    else
                    {
                        await stream.CopyToAsync(ostream, token);
                    }
                }
                catch (IOException)
                {
                    if (!token.IsCancellationRequested)
                    {
                        throw;
                    }
                    else
                    {
                        throw new OperationCanceledException(token);
                    }
                }
                finally
                {
                    try
                    {
                        await ostream.FlushAsync();
                        ostream.Close();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
            });
        }

        public override Task PreserveModifiedTimeAsync(string filePath, DateTimeOffset? modifiedTime, CancellationToken token)
        {
            if (modifiedTime.HasValue)
            {
                var fullPath = GetFullPath(NormalizePath(filePath));

                File.SetLastWriteTimeUtc(fullPath, modifiedTime.Value.UtcDateTime);
            }
            return Task.CompletedTask;
        }

        protected override async Task<IList<string>> FindFileVersionsAsync(string filePath, CancellationToken token)
        {
            await Task.Yield();
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            var directory = Path.GetDirectoryName(GetFullPath(NormalizePath(filePath)));
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return new List<string>();
            }

            // Case-insensitive on every platform to match the behavior of other providers
            var enumerationOptions = new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = false,
                IgnoreInaccessible = true
            };

            return Directory.EnumerateFiles(directory, fileNameWithoutExtension + "*" + extension, enumerationOptions)
                .Where(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _connection?.Dispose();
                _connection = null;
            }
            base.Dispose(disposing);
        }
    }
}
