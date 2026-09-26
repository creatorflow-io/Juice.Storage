using System;
using System.IO;
using System.Threading.Tasks;
using Juice.Storage.Abstractions;
using Juice.Storage.Local;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Juice.Storage.Tests
{
    public sealed class CrossPlatformStorageTest : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "juice-storage-test-" + Guid.NewGuid().ToString("N"));

        private LocalStorageProvider CreateProvider(string? uri = default, INetworkConnectionFactory? factory = default)
        {
            var provider = new LocalStorageProvider(NullLogger<LocalStorageProvider>.Instance, factory);
            provider.Configure(new StorageEndpoint(uri ?? _root, default));
            return provider;
        }

        [Fact(DisplayName = "Backslash separated names are stored in sub directories")]
        public async Task Backslash_names_should_create_directories_Async()
        {
            using var storage = CreateProvider();

            var created = await storage.CreateAsync(@"dir\sub\a.txt", new CreateFileOptions(), default);

            Assert.Equal("dir/sub/a.txt", created);
            Assert.True(File.Exists(Path.Combine(_root, "dir", "sub", "a.txt")));
            Assert.True(await storage.ExistsAsync(@"dir\sub\a.txt", default));
            Assert.True(await storage.ExistsAsync("dir/sub/a.txt", default));
        }

        [Theory(DisplayName = "Paths escaping the storage root are rejected")]
        [InlineData("../a.txt")]
        [InlineData(@"..\a.txt")]
        [InlineData("dir/../../a.txt")]
        [InlineData("dir/.../a.txt")]
        [InlineData(@"C:\a.txt")]
        [InlineData("C:a.txt")]
        [InlineData("")]
        [InlineData("/")]
        public async Task Path_outside_root_should_be_rejected_Async(string filePath)
        {
            using var storage = CreateProvider();

            await Assert.ThrowsAsync<ArgumentException>(() => storage.CreateAsync(filePath, new CreateFileOptions(), default));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.ReadAsync(filePath, default));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(filePath, default));
        }

        [Fact(DisplayName = "Rooted paths are relative to the storage root")]
        public async Task Rooted_path_should_stay_in_root_Async()
        {
            using var storage = CreateProvider();

            var created = await storage.CreateAsync("/etc/juice.txt", new CreateFileOptions(), default);

            Assert.Equal("etc/juice.txt", created);
            Assert.True(File.Exists(Path.Combine(_root, "etc", "juice.txt")));
        }

        [Fact(DisplayName = "Copy number continues from the highest copy of the same name")]
        public async Task Copy_number_should_ignore_other_names_Async()
        {
            using var storage = CreateProvider();
            var options = new CreateFileOptions { FileExistsBehavior = FileExistsBehavior.AscendedCopyNumber };

            await storage.CreateAsync("abcdef(5).txt", options, default);
            await storage.CreateAsync("abc(3).txt", options, default);
            await storage.CreateAsync("abc.txt", options, default);

            Assert.Equal("abc(4).txt", await storage.CreateAsync("abc.txt", options, default));
            Assert.Equal("dir/abc.txt", await storage.CreateAsync(@"dir\abc.txt", options, default));
            Assert.Equal("dir/abc(1).txt", await storage.CreateAsync(@"dir\abc.txt", options, default));
        }

        [Fact(DisplayName = "Write, resume, read and preserve modified time")]
        public async Task Write_resume_read_Async()
        {
            using var storage = CreateProvider();

            var created = await storage.CreateAsync("data/file.bin", new CreateFileOptions(), default);
            await storage.WriteAsync(created, new MemoryStream(new byte[] { 1, 2, 3 }), 0, new TransferOptions(), default);
            await storage.WriteAsync(created, new MemoryStream(new byte[] { 4, 5 }), 3, new TransferOptions(), default);
            var modified = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
            await storage.PreserveModifiedTimeAsync(created, modified, default);

            Assert.Equal(5, await storage.FileSizeAsync(created, default));
            using (var stream = await storage.ReadAsync(created, default))
            {
                var buffer = new byte[5];
                Assert.Equal(5, await stream.ReadAsync(buffer, 0, 5));
                Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, buffer);
            }
            Assert.Equal(modified.UtcDateTime, File.GetLastWriteTimeUtc(Path.Combine(_root, "data", "file.bin")));

            await storage.DeleteAsync(created, default);
            Assert.False(await storage.ExistsAsync(created, default));
        }

        [Fact(DisplayName = "Network share without a connection factory fails on non-Windows")]
        public async Task Network_share_without_factory_should_throw_on_unix_Async()
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "UNC paths are accessible natively on Windows");

            var cwdEntries = Directory.GetFileSystemEntries(Directory.GetCurrentDirectory()).Length;
            using var storage = new LocalStorageProvider(NullLogger<LocalStorageProvider>.Instance);
            storage.Configure(new StorageEndpoint(@"\\server\share\dir", @"\\server", "user", "secret", Protocol.Smb));

            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => storage.CreateAsync("a.txt", new CreateFileOptions(), default));
            // nothing must be written to a local directory named after the UNC path
            Assert.Equal(cwdEntries, Directory.GetFileSystemEntries(Directory.GetCurrentDirectory()).Length);
        }

        [Theory(DisplayName = "Parse network paths")]
        [InlineData(@"\\server\share\dir\sub", "server", "share", "dir/sub")]
        [InlineData(@"\\server\share", "server", "share", "")]
        [InlineData(@"\\server", "server", "", "")]
        [InlineData("smb://server/share/dir", "server", "share", "dir")]
        [InlineData("//server/share/dir/", "server", "share", "dir")]
        public void Parse_network_path(string path, string server, string share, string subPath)
        {
            Assert.True(UncPath.TryParse(path, out var s, out var sh, out var sub));
            Assert.Equal(server, s);
            Assert.Equal(share, sh);
            Assert.Equal(subPath, sub);
        }

        [Theory(DisplayName = "Detect network paths")]
        [InlineData(@"\\server\share", true)]
        [InlineData("smb://server/share", true)]
        [InlineData("/mnt/share", false)]
        [InlineData(@"C:\Storage", false)]
        [InlineData(@"\\server\..\x", true)]
        public void Detect_network_path(string path, bool expected)
        {
            Assert.Equal(expected, UncPath.IsNetworkPath(path));
        }

        [Fact(DisplayName = "Parent segments in network paths are rejected")]
        public void Parse_network_path_with_parent_segment()
        {
            Assert.False(UncPath.TryParse(@"\\server\share\..\x", out _, out _, out _));
        }

        [Fact(DisplayName = "Convert network paths to Windows UNC")]
        public void Convert_to_windows_unc()
        {
            Assert.Equal(@"\\server\share\dir\sub", UncPath.ToWindowsUnc("smb://server/share/dir/sub"));
            Assert.Equal(@"\\server", UncPath.ToWindowsUnc(@"\\server"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}
