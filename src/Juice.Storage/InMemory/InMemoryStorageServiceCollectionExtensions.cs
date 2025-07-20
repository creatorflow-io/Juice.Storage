using Juice.Storage;
using Juice.Storage.Abstractions;
using Juice.Storage.BackgroundTasks;
using Juice.Storage.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class InMemoryStorageServiceCollectionExtensions
    {
        public static IServiceCollection AddInMemoryUploadManager(this IServiceCollection services, IConfiguration configuration, Action<UploadOptions>? configure = default)
        {
            services.Configure<InMemoryStorageOptions>(configuration);
            services.AddScoped<IStorageRepository, InMemoryStorageRepository>();
            if (configure != null)
            {
                services.AddDefaultUploadManager<UploadFileInfo>(configure);
            }
            else
            {
                services.AddDefaultUploadManager<UploadFileInfo>(configuration);
            }
            services.AddSingleton<IUploadRepository<UploadFileInfo>, InMemoryUploadRepository>();

            return services;
        }

        public static IServiceCollection AddInMemoryStorageMaintainServices(this IServiceCollection services,
            IConfiguration configuration, string[] identities,
            Action<StorageMaintainOptions>? configure = default)
            => services.AddStorageMaintainServices<UploadFileInfo>(configuration, identities, configure);

    }
}
