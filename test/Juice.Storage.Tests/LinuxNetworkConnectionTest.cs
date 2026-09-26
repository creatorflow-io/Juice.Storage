using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Juice.Storage.Abstractions;
using Juice.Storage.Local;
using Juice.Storage.Local.Linux;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using CommandResult = Juice.Storage.Local.Linux.LinuxNetworkConnectionFactory.CommandResult;

namespace Juice.Storage.Tests
{
    public sealed class LinuxNetworkConnectionTest : IDisposable
    {
        private readonly string _mountRoot = Path.Combine(Path.GetTempPath(), "juice-mount-test-" + Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Simulates mount/umount by editing an in-memory mount table.
        /// </summary>
        private sealed class FakeSystem
        {
            public readonly List<string> MountedPoints = new();
            public readonly List<(string Command, IReadOnlyList<string> Args, bool Sudo)> Calls = new();
            public string? CredentialsFileContent;
            public UnixFileMode? CredentialsFileMode;
            public string? CredentialsFile;
            public int MountExitCode;

            public string ReadMountTable()
                => string.Join("\n", MountedPoints.Select(m => $"//server/share {m.Replace(" ", "\\040")} cifs rw 0 0"));

            public CommandResult Run(string command, IReadOnlyList<string> args, bool sudo)
            {
                Calls.Add((command, args, sudo));
                switch (command)
                {
                    case "id":
                        return new CommandResult(0, "1000\n", "");
                    case "mount":
                        var options = args[args.ToList().IndexOf("-o") + 1];
                        CredentialsFile = options.Split(',').Single(o => o.StartsWith("credentials=")).Substring("credentials=".Length);
                        CredentialsFileContent = File.ReadAllText(CredentialsFile);
                        CredentialsFileMode = File.GetUnixFileMode(CredentialsFile);
                        if (MountExitCode != 0)
                        {
                            return new CommandResult(MountExitCode, "", "mount error(13): Permission denied");
                        }
                        MountedPoints.Add(args[3]);
                        return new CommandResult(0, "", "");
                    case "umount":
                        MountedPoints.Remove(args[0]);
                        return new CommandResult(0, "", "");
                    default:
                        return new CommandResult(0, "", "");
                }
            }
        }

        private LinuxNetworkConnectionFactory CreateFactory(FakeSystem system, Action<LinuxNetworkConnectionOptions>? configure = default)
        {
            var options = new LinuxNetworkConnectionOptions { MountRoot = _mountRoot };
            configure?.Invoke(options);
            return new LinuxNetworkConnectionFactory(options, NullLogger.Instance, system.ReadMountTable, system.Run);
        }

        private static StorageEndpoint Endpoint(string uri = @"\\Server\Share\Dir")
            => new StorageEndpoint(uri, @"\\Server", "user", "p@ss,word", Protocol.Smb);

        [Fact(DisplayName = "Mount share with a protected credentials file")]
        public void Connect_should_mount_share()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
            var system = new FakeSystem();
            using var factory = CreateFactory(system, o => o.MountOptions = "vers=3.0");

            using var connection = factory.Connect(Endpoint(), new NetworkCredential("user", "p@ss,word", "CORP"));

            var mountPoint = Path.Combine(_mountRoot, "server", "share");
            var mount = system.Calls.Single(c => c.Command == "mount");
            Assert.False(mount.Sudo);
            Assert.Equal(new[] { "-t", "cifs", "//Server/Share", mountPoint, "-o" }, mount.Args.Take(5));
            Assert.Contains("uid=1000,gid=1000", mount.Args[5]);
            Assert.EndsWith(",vers=3.0", mount.Args[5]);
            Assert.DoesNotContain(mount.Args, a => a.Contains("p@ss"));

            Assert.Equal("username=user\npassword=p@ss,word\ndomain=CORP\n", system.CredentialsFileContent);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, system.CredentialsFileMode);
            Assert.False(File.Exists(system.CredentialsFile));

            Assert.Equal(Path.Combine(mountPoint, "Dir", "Sub"), connection.ResolvePath(@"\\Server\Share\Dir\Sub"));
            Assert.Equal(Path.Combine(mountPoint, "Dir"), factory.ResolvePath("smb://server/share/Dir"));
        }

        [Fact(DisplayName = "Reuse mounted share and unmount on dispose")]
        public void Connect_should_reuse_mount()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
            var system = new FakeSystem();
            var factory = CreateFactory(system, o => o.UseSudo = true);

            factory.Connect(Endpoint(), new NetworkCredential("user", "pass")).Dispose();
            factory.Connect(Endpoint(@"\\server\share\Other"), new NetworkCredential("user", "pass")).Dispose();

            Assert.Single(system.Calls, c => c.Command == "mount");
            Assert.All(system.Calls.Where(c => c.Command is "mount" or "mkdir"), c => Assert.True(c.Sudo));
            Assert.Single(system.MountedPoints);

            factory.Dispose();
            Assert.Empty(system.MountedPoints);
            Assert.True(system.Calls.Single(c => c.Command == "umount").Sudo);
        }

        [Fact(DisplayName = "Pre-mounted shares are not mounted nor unmounted")]
        public void Premounted_share_should_be_used()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
            var system = new FakeSystem();
            system.MountedPoints.Add(Path.Combine(_mountRoot, "server", "share") + "/");
            var factory = CreateFactory(system);

            Assert.Equal(Path.Combine(_mountRoot, "server", "share", "x"), factory.ResolvePath(@"\\server\share\x"));
            factory.Connect(Endpoint(), new NetworkCredential("user", "pass"));
            factory.Dispose();

            Assert.DoesNotContain(system.Calls, c => c.Command is "mount" or "umount");
        }

        [Fact(DisplayName = "Unmounted share cannot be resolved")]
        public void Resolve_unmounted_share_should_throw()
        {
            var factory = CreateFactory(new FakeSystem());
            Assert.Throws<DirectoryNotFoundException>(() => factory.ResolvePath(@"\\server\share\x"));
            Assert.Throws<ArgumentException>(() => factory.ResolvePath(@"\\server"));
        }

        [Fact(DisplayName = "Mount failure reports the mount error")]
        public void Mount_failure_should_throw()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
            var system = new FakeSystem { MountExitCode = 32 };
            using var factory = CreateFactory(system);

            var ex = Assert.Throws<IOException>(() => factory.Connect(Endpoint(), new NetworkCredential("user", "pass")));
            Assert.Contains("Permission denied", ex.Message);
            Assert.DoesNotContain("pass", ex.Message.Replace("Permission", ""));
            Assert.False(File.Exists(system.CredentialsFile));
        }

        [Fact(DisplayName = "Local storage provider writes into the mounted share")]
        public async Task Provider_should_write_to_mount_point_Async()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
            var system = new FakeSystem();
            using var factory = CreateFactory(system);
            using var storage = new LocalStorageProvider(NullLogger<LocalStorageProvider>.Instance, factory);
            storage.Configure(Endpoint());

            var created = await storage.CreateAsync(@"a\b.txt", new CreateFileOptions(), default);

            Assert.Equal("a/b.txt", created);
            Assert.True(File.Exists(Path.Combine(_mountRoot, "server", "share", "Dir", "a", "b.txt")));
        }

        [Fact(DisplayName = "Parse mount table escapes")]
        public void Mount_table_should_unescape()
        {
            var table = "proc /proc proc rw 0 0\n//server/share /mnt/my\\040share cifs rw 0 0\n";
            Assert.True(MountTable.IsMountPoint(table, "/mnt/my share"));
            Assert.True(MountTable.IsMountPoint(table, "/mnt/my share/"));
            Assert.False(MountTable.IsMountPoint(table, "/mnt/my"));
            Assert.Equal(@"a\b", MountTable.Unescape(@"a\134b"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_mountRoot))
                {
                    Directory.Delete(_mountRoot, true);
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}
