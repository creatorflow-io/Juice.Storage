using System;
using System.Collections.Generic;
using System.Linq;
using Juice.Storage.Abstractions;
using Juice.Storage.InMemory;
using Juice.Storage.Local;
using Juice.Storage.Local.Linux;
using Juice.Storage.Local.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Juice.Storage.Tests
{
    public class StorageBuilderTest
    {
        private static IConfiguration Configuration => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storages:0:WebBasePath"] = "/storage",
                ["Storages:0:Endpoints:0:Uri"] = "/tmp/storage",
            })
            .Build();

        [Fact(DisplayName = "Builder registers core services and providers once")]
        public void Builder_should_register_services()
        {
            var services = new ServiceCollection().AddLogging();

            services.AddStorage(storage => storage
                .AddLocalStorageProviders()
                .AddLocalDiskProvider()
                .AddFtpProvider()
                .AddInMemoryUploadManager(Configuration)
                .AddInMemoryStorageMaintainServices(Configuration, new[] { "/storage" })
                .AddDefaultDownloadManager<UploadFileInfo>(Configuration));

            Assert.Contains(services, d => d.ServiceType == typeof(IStorageResolver));
            Assert.Contains(services, d => d.ServiceType == typeof(IUploadManager));
            Assert.Contains(services, d => d.ServiceType == typeof(IDownloadManager));
            Assert.Contains(services, d => d.ServiceType == typeof(IStorageRepository));
            Assert.Single(services, d => d.ServiceType == typeof(IHostedService));

            using var serviceProvider = services.BuildServiceProvider();
            var providers = serviceProvider.GetServices<IStorageProvider>().Select(p => p.GetType()).ToArray();
            Assert.Equal(new[] { typeof(LocalStorageProvider), typeof(FTPStorageProvider) }, providers);
        }

        [Fact(DisplayName = "Builder registers the network connection of the current platform only")]
        public void Builder_should_register_platform_network_connection()
        {
            var services = new ServiceCollection().AddLogging();

            services.AddStorage(storage => storage
                .AddLocalDiskProvider()
                .AddWindowsNetworkConnection()
                .AddLinuxNetworkConnection(options => options.MountRoot = "/mnt/test"));

            using var serviceProvider = services.BuildServiceProvider();
            var factory = serviceProvider.GetService<INetworkConnectionFactory>();

            if (OperatingSystem.IsWindows())
            {
                Assert.IsType<WindowsNetworkConnectionFactory>(factory);
            }
            else if (OperatingSystem.IsLinux())
            {
                Assert.IsType<LinuxNetworkConnectionFactory>(factory);
                Assert.Equal("/mnt/test", serviceProvider.GetRequiredService<IOptions<LinuxNetworkConnectionOptions>>().Value.MountRoot);
            }
            else
            {
                Assert.Null(factory);
            }
        }

        [Fact(DisplayName = "Builder registers custom providers and repositories")]
        public void Builder_should_register_custom_services()
        {
            var services = new ServiceCollection().AddLogging();

#pragma warning disable CS0618 // Type or member is obsolete
            services.AddStorage(storage => storage
                .AddInMemoryStorageRepository(Configuration)
                .AddStorageRepository<FakeStorageRepository>()
                .AddInMemoryStorageProvider()
                .AddInMemoryStorageProvider());
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.Single(services, d => d.ServiceType == typeof(IStorageRepository));
            using var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            Assert.IsType<FakeStorageRepository>(scope.ServiceProvider.GetRequiredService<IStorageRepository>());
            Assert.Single(scope.ServiceProvider.GetServices<IStorageProvider>());
        }

        private class FakeStorageRepository : IStorageRepository
        {
            public System.Threading.Tasks.Task<bool> ExistsAsync(string identity)
                => System.Threading.Tasks.Task.FromResult(false);
            public System.Threading.Tasks.Task<IEnumerable<StorageEndpoint>> GetEndpointsAsync(string identity)
                => System.Threading.Tasks.Task.FromResult(Enumerable.Empty<StorageEndpoint>());
        }
    }
}
