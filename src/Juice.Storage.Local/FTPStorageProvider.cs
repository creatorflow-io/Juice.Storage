using System.Net;
using System.Text.RegularExpressions;
using FluentFTP;
using Juice.Storage.Abstractions;

namespace Juice.Storage.Local
{
    public class FTPStorageProvider : StorageProviderBase
    {
        private AsyncFtpClient? _client;

        public const string FtpAddressPattern = @"^(?<protocol>ftp[s]{0,1}:\/\/)*(?<host>[\w\.]+)[:]*(?<port>[0-9]+)*(?<working>[\/][^\n]+)*$";

        private string? _workingDirectory;

        public override Protocol[] Protocols => new Protocol[] { Protocol.Ftp };

        public override IStorageProvider Configure(StorageEndpoint endpoint, int? priority = default)
        {
            StorageEndpoint = endpoint;
            if (priority.HasValue)
            {
                Priority = priority.Value;
            }

            Init();

            if (!string.IsNullOrWhiteSpace(endpoint.Identity))
            {
                return this.WithCredential(new NetworkCredential(endpoint.Identity, endpoint.Password));
            }

            return this;
        }

        public override IStorageProvider WithCredential(NetworkCredential credential)
        {
            CheckEndpoint();
            base.WithCredential(credential);

            if (_client == null)
            {
                Init();
            }

            _client.Credentials = Credential;

            return this;
        }

        private void Init()
        {
            if (StorageEndpoint == null)
            {
                throw new Exception("Storage endpoint is not set. Please call Configure method first.");
            }
            var match = new Regex(FtpAddressPattern, RegexOptions.IgnoreCase).Match(StorageEndpoint.Uri);
            if (!match.Success)
            {
                throw new ArgumentException("Ftp URI does not match. Please try this patterns: ftp://localhost, ftps://localhost:2121, locahost/working/dir...");
            }
            _client = new AsyncFtpClient(match.Groups["host"].Value);
            if (match.Groups["port"].Value != null && int.TryParse(match.Groups["port"].Value, out var port))
            {
                _client.Port = port;
            }

            _workingDirectory = match.Groups["working"].Value;
        }

        private async Task EnsureConnectedAsync(CancellationToken token)
        {
            if (_client == null)
            {
                throw new Exception("Client has not initialized. Please call Configure method first.");
            }
            if (_client.IsDisposed)
            {
                Init();
            }
            if(!_client.IsConnected)
            {
                await _client.Connect(token);
            }
            if (!string.IsNullOrEmpty(_workingDirectory))
            {
                if (!await _client.DirectoryExists(_workingDirectory, token))
                {
                    await _client.CreateDirectory(_workingDirectory, token);
                }
                await _client.SetWorkingDirectory(_workingDirectory, token);
            }
        }

        public override async Task<string> CreateAsync(string filePath, CreateFileOptions options, CancellationToken token)
        {
            await EnsureConnectedAsync(token);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !await _client!.DirectoryExists(directory))
            {
                await _client.CreateDirectory(directory);
            }

            if (!await _client!.FileExists(filePath, token))
            {
                await (await _client.OpenWrite(filePath)).DisposeAsync();
                await _client.GetReply(token);
                return filePath;
            }
            var fileExistsBehavior = options?.FileExistsBehavior ?? FileExistsBehavior.RaiseError;
            switch (fileExistsBehavior)
            {
                case FileExistsBehavior.RaiseError:
                    throw new IOException("File is already exists.");
                case FileExistsBehavior.Replace:
                    File.Delete(filePath);

                    await (await _client.OpenWrite(filePath)).DisposeAsync();
                    await _client.GetReply(token);
                    return filePath;

                case FileExistsBehavior.AscendedCopyNumber:
                    var newPath = await GetNameAscendedCopyNumberAsync(filePath, default, token);
                    await (await _client.OpenWrite(newPath)).DisposeAsync();
                    await _client.GetReply(token);
                    return newPath;
                default: throw new IOException("File is already exists.");
            }
        }

        public override async Task DeleteAsync(string filePath, CancellationToken token)
        {
            await EnsureConnectedAsync(token).ConfigureAwait(false);
            if (await _client!.FileExists(filePath, token))
            {
                await _client.DeleteFile(filePath, token);
            }
        }

        public override async Task<bool> ExistsAsync(string filePath, CancellationToken token)
        {
            await EnsureConnectedAsync(token);
            return await _client!.FileExists(filePath, token);
        }

        public override async Task<long> FileSizeAsync(string filePath, CancellationToken token)
        {
            await EnsureConnectedAsync(token);
            return await _client!.GetFileSize(filePath, -1, token);
        }

        public override async Task<Stream> ReadAsync(string filePath, CancellationToken token)
        {
            await EnsureConnectedAsync(token);
            return await _client!.OpenRead(filePath);
        }

        public override async Task WriteAsync(string filePath, Stream stream, long offset, TransferOptions options, CancellationToken token)
        {
            await EnsureConnectedAsync(token);

            if (offset > 0)
            {
                var size = await FileSizeAsync(filePath, token);
                if (offset != size)
                {
                    throw new Exception("File cannot be resume from position");
                }

                using var ostream = await _client!.OpenAppend(filePath);
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
            }
            else
            {
                using var ostream = await _client!.OpenWrite(filePath);

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
                catch (Exception)
                {
                    throw;
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

            }


        }

        public override async Task PreserveModifiedTimeAsync(string filePath, DateTimeOffset? modifiedTime, CancellationToken token)
        {
            if (modifiedTime.HasValue)
            {
                await EnsureConnectedAsync(token);
                await _client!.SetModifiedTime(filePath, modifiedTime.Value.UtcDateTime, token);
            }
        }

        protected override async Task<IList<string>> FindFileVersionsAsync(string filePath, CancellationToken token)
        {
            await Task.Yield();

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            var directory = Path.GetDirectoryName(filePath);

            var files = string.IsNullOrEmpty(directory) ?
                await _client!.GetNameListing(token)
                : await _client!.GetNameListing(directory, token);

            return files
                .Where(f => Path.GetFileNameWithoutExtension(f).StartsWith(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase)
                    && Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_client != null)
                {
                    if (_client.IsConnected)
                    {
                        _client.Disconnect();
                    }
                    _client.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
