using Juice.Storage;
using Juice.Storage.Abstractions;
using Juice.Storage.BackgroundTasks;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IStorageBuilder"/> versions of <see cref="InMemoryStorageServiceCollectionExtensions"/>.
    /// </summary>
    public static class InMemoryStorageBuilderExtensions
    {
        public static IStorageBuilder AddInMemoryStorageRepository(this IStorageBuilder builder, IConfiguration configuration)
        {
            builder.Services.AddInMemoryStorageRepository(configuration);
            return builder;
        }

        public static IStorageBuilder AddInMemoryUploadRepository<T>(this IStorageBuilder builder)
            where T : class, IFile, new()
        {
            builder.Services.AddInMemoryUploadRepository<T>();
            return builder;
        }

        public static IStorageBuilder AddInMemoryUploadManager<T>(this IStorageBuilder builder, IConfiguration configuration,
            Action<UploadOptions>? configure = default)
            where T : class, IFile, new()
        {
            builder.Services.AddInMemoryUploadManager<T>(configuration, configure);
            return builder;
        }

        public static IStorageBuilder AddInMemoryUploadManager(this IStorageBuilder builder, IConfiguration configuration,
            Action<UploadOptions>? configure = default)
        {
            builder.Services.AddInMemoryUploadManager(configuration, configure);
            return builder;
        }

        public static IStorageBuilder AddInMemoryStorageMaintainServices(this IStorageBuilder builder,
            IConfiguration configuration, string[] identities,
            Action<StorageMaintainOptions>? configure = default)
        {
            builder.Services.AddInMemoryStorageMaintainServices(configuration, identities, configure);
            return builder;
        }
    }
}
