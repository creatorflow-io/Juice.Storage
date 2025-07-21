using Juice.Storage;
using Juice.Storage.Abstractions;
using Juice.Storage.BackgroundTasks;
using Juice.Storage.InMemory;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class InMemoryStorageServiceCollectionExtensions
    {
        public static IServiceCollection AddInMemoryUploadManager<T>(this IServiceCollection services, IConfiguration configuration, Action<UploadOptions>? configure = default)
            where T : class, IFile, new()
        {
            services.Configure<InMemoryStorageOptions>(configuration);
            services.AddScoped<IStorageRepository, InMemoryStorageRepository>();
            if (configure != null)
            {
                services.AddDefaultUploadManager<T>(configure);
            }
            else
            {
                services.AddDefaultUploadManager<T>(configuration);
            }
            services.AddSingleton<IUploadRepository<T>, InMemoryUploadRepository<T>>();

            return services;
        }

        public static IServiceCollection AddInMemoryUploadManager(this IServiceCollection services, IConfiguration configuration, Action<UploadOptions>? configure = default)
            => services.AddInMemoryUploadManager<UploadFileInfo>(configuration, configure);


        public static IServiceCollection AddInMemoryStorageMaintainServices(this IServiceCollection services,
            IConfiguration configuration, string[] identities,
            Action<StorageMaintainOptions>? configure = default)
            => services.AddStorageMaintainServices<UploadFileInfo>(configuration, identities, configure);

    }
}
