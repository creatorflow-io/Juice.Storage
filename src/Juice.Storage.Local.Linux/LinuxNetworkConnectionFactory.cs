using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using Juice.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Juice.Storage.Local.Linux
{
    /// <summary>
    /// Mounts network shares with mount.cifs at {MountRoot}/{server}/{share} and maps network paths to the mount point.
    /// <para>Mounting requires root, CAP_SYS_ADMIN (containers) or <see cref="LinuxNetworkConnectionOptions.UseSudo"/>,
    /// and the cifs-utils package.</para>
    /// <para>Mounts are shared by all storage providers and kept until the factory is disposed.</para>
    /// </summary>
    public sealed class LinuxNetworkConnectionFactory : INetworkConnectionFactory, IDisposable
    {
        internal record CommandResult(int ExitCode, string StandardOutput, string StandardError);

        private readonly LinuxNetworkConnectionOptions _options;
        private readonly ILogger _logger;
        private readonly Func<string> _readMountTable;
        private readonly Func<string, IReadOnlyList<string>, bool, CommandResult> _runCommand;

        private readonly ConcurrentDictionary<string, object> _locks = new();
        private readonly ConcurrentDictionary<string, byte> _mountedByFactory = new();
        private (int Uid, int Gid)? _ids;
        private bool _disposed;

        public LinuxNetworkConnectionFactory(IOptions<LinuxNetworkConnectionOptions> options,
            ILogger<LinuxNetworkConnectionFactory> logger)
            : this(options.Value, logger, MountTable.Read, null)
        {
        }

        internal LinuxNetworkConnectionFactory(LinuxNetworkConnectionOptions options, ILogger logger,
            Func<string> readMountTable,
            Func<string, IReadOnlyList<string>, bool, CommandResult>? runCommand)
        {
            _options = options;
            _logger = logger;
            _readMountTable = readMountTable;
            _runCommand = runCommand ?? RunCommand;
        }

        public INetworkConnection Connect(StorageEndpoint endpoint, NetworkCredential credential)
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException($"{nameof(LinuxNetworkConnectionFactory)} is only supported on Linux.");
            }
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LinuxNetworkConnectionFactory));
            }

            var (server, share, _) = Parse(endpoint.Uri);
            var mountPoint = GetMountPoint(server, share);

            lock (_locks.GetOrAdd(mountPoint, _ => new object()))
            {
                if (IsMounted(mountPoint))
                {
                    _logger.LogDebug("Network share //{Server}/{Share} is already mounted at {MountPoint}", server, share, mountPoint);
                }
                else
                {
                    Mount(server, share, mountPoint, credential);
                    _mountedByFactory[mountPoint] = 0;
                }
            }
            return new LinuxNetworkConnection(this);
        }

        public string ResolvePath(string networkPath)
        {
            var (server, share, _) = Parse(networkPath);
            var mountPoint = GetMountPoint(server, share);
            if (!IsMounted(mountPoint))
            {
                throw new DirectoryNotFoundException($"Network share //{server}/{share} is not mounted at {mountPoint}. " +
                    "Configure credentials for the storage endpoint or mount the share there (ex: /etc/fstab).");
            }
            return MapPath(networkPath);
        }

        /// <summary>
        /// Map a network path to its location under the mount point, without checking the mount.
        /// </summary>
        internal string MapPath(string networkPath)
        {
            var (server, share, subPath) = Parse(networkPath);
            var mountPoint = GetMountPoint(server, share);
            return string.IsNullOrEmpty(subPath) ? mountPoint : Path.Combine(mountPoint, subPath);
        }

        internal string GetMountPoint(string server, string share)
            => Path.Combine(_options.MountRoot, server.ToLowerInvariant(), share.ToLowerInvariant());

        private static (string Server, string Share, string SubPath) Parse(string networkPath)
        {
            if (!UncPath.TryParse(networkPath, out var server, out var share, out var subPath)
                || string.IsNullOrEmpty(share))
            {
                throw new ArgumentException($"'{networkPath}' is not a network share path. Expected \\\\server\\share[\\dir] or smb://server/share[/dir].", nameof(networkPath));
            }
            if (server.Any(char.IsControl) || share.Any(char.IsControl) || server.StartsWith("-") || share.StartsWith("-"))
            {
                throw new ArgumentException($"'{networkPath}' contains invalid characters.", nameof(networkPath));
            }
            return (server, share, subPath);
        }

        private bool IsMounted(string mountPoint)
            => MountTable.IsMountPoint(_readMountTable(), mountPoint);

        [SupportedOSPlatform("linux")]
        private void Mount(string server, string share, string mountPoint, NetworkCredential credential)
        {
            CreateMountPoint(mountPoint);

            var credentialsFile = WriteCredentialsFile(credential);
            try
            {
                var (uid, gid) = GetIds();
                var options = new List<string>
                {
                    "credentials=" + credentialsFile,
                    "uid=" + uid,
                    "gid=" + gid,
                    "file_mode=" + _options.FileMode,
                    "dir_mode=" + _options.DirMode
                };
                if (!string.IsNullOrWhiteSpace(_options.MountOptions))
                {
                    options.Add(_options.MountOptions!.Trim().Trim(','));
                }

                var result = _runCommand(_options.MountCommand,
                    new[] { "-t", "cifs", $"//{server}/{share}", mountPoint, "-o", string.Join(",", options) },
                    _options.UseSudo);

                if (result.ExitCode != 0)
                {
                    throw new IOException($"Failed to mount //{server}/{share} at {mountPoint} (exit code {result.ExitCode}): {result.StandardError.Trim()}");
                }
                _logger.LogInformation("Mounted network share //{Server}/{Share} at {MountPoint}", server, share, mountPoint);
            }
            finally
            {
                try
                {
                    File.Delete(credentialsFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete the temporary credentials file {File}", credentialsFile);
                }
            }
        }

        private void CreateMountPoint(string mountPoint)
        {
            if (Directory.Exists(mountPoint))
            {
                return;
            }
            if (_options.UseSudo)
            {
                var result = _runCommand("mkdir", new[] { "-p", mountPoint }, true);
                if (result.ExitCode != 0)
                {
                    throw new IOException($"Failed to create mount point {mountPoint}: {result.StandardError.Trim()}");
                }
            }
            else
            {
                Directory.CreateDirectory(mountPoint);
            }
        }

        /// <summary>
        /// Write the credentials to a file only readable by the current user,
        /// so the password is not visible in the process list.
        /// </summary>
        [SupportedOSPlatform("linux")]
        private string WriteCredentialsFile(NetworkCredential credential)
        {
            var values = new[] { credential.UserName, credential.Password, credential.Domain };
            if (values.Any(v => v != null && (v.Contains('\n') || v.Contains('\r'))))
            {
                throw new ArgumentException("Network credential must not contain line breaks.", nameof(credential));
            }

            var content = new StringBuilder()
                .Append("username=").Append(credential.UserName).Append('\n')
                .Append("password=").Append(credential.Password).Append('\n');
            if (!string.IsNullOrEmpty(credential.Domain))
            {
                content.Append("domain=").Append(credential.Domain).Append('\n');
            }

            var path = Path.Combine(Path.GetTempPath(), $"juice-cifs-{Guid.NewGuid():N}.cred");
            if (path.Contains(','))
            {
                throw new IOException($"Temporary path '{path}' must not contain ','.");
            }
#if NET7_0_OR_GREATER
            using (var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = System.IO.FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            }))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content.ToString());
            }
#else
            using (new FileStream(path, System.IO.FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var chmod = _runCommand("chmod", new[] { "600", path }, false);
            if (chmod.ExitCode != 0)
            {
                File.Delete(path);
                throw new IOException($"Failed to protect the temporary credentials file: {chmod.StandardError.Trim()}");
            }
            File.WriteAllText(path, content.ToString());
#endif
            return path;
        }

        private (int Uid, int Gid) GetIds()
        {
            if (_ids.HasValue)
            {
                return _ids.Value;
            }
            var uid = _options.Uid ?? ReadId("-u");
            var gid = _options.Gid ?? ReadId("-g");
            _ids = (uid, gid);
            return _ids.Value;
        }

        private int ReadId(string flag)
        {
            var result = _runCommand("id", new[] { flag }, false);
            if (result.ExitCode != 0 || !int.TryParse(result.StandardOutput.Trim(), out var id))
            {
                throw new IOException($"Could not read the current user id (id {flag}): {result.StandardError.Trim()}. Configure Uid/Gid options.");
            }
            return id;
        }

        private CommandResult RunCommand(string command, IReadOnlyList<string> arguments, bool sudo)
        {
            var startInfo = new ProcessStartInfo(sudo ? _options.SudoCommand : command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            if (sudo)
            {
                // non-interactive: fail instead of prompting for a password
                startInfo.ArgumentList.Add("-n");
                startInfo.ArgumentList.Add(command);
            }
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new IOException($"Could not start '{startInfo.FileName}'.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)_options.CommandTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception)
                {
                    // ignored
                }
                throw new TimeoutException($"'{command}' did not complete within {_options.CommandTimeout.TotalSeconds:n0}s.");
            }
            process.WaitForExit();
            return new CommandResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (!_options.UnmountOnDispose)
            {
                return;
            }
            foreach (var mountPoint in _mountedByFactory.Keys)
            {
                try
                {
                    var result = _runCommand(_options.UnmountCommand, new[] { mountPoint }, _options.UseSudo);
                    if (result.ExitCode != 0)
                    {
                        _logger.LogWarning("Failed to unmount {MountPoint} (exit code {ExitCode}): {Error}", mountPoint, result.ExitCode, result.StandardError.Trim());
                    }
                    else
                    {
                        _logger.LogInformation("Unmounted network share at {MountPoint}", mountPoint);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unmount {MountPoint}", mountPoint);
                }
            }
            _mountedByFactory.Clear();
        }

        /// <summary>
        /// Mounts are shared between providers, so disposing a connection keeps the share mounted.
        /// </summary>
        private sealed class LinuxNetworkConnection : INetworkConnection
        {
            private readonly LinuxNetworkConnectionFactory _factory;

            public LinuxNetworkConnection(LinuxNetworkConnectionFactory factory)
            {
                _factory = factory;
            }

            public string ResolvePath(string networkPath) => _factory.MapPath(networkPath);

            public void Dispose()
            {
            }
        }
    }
}
