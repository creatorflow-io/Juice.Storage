using Juice.Storage;
using Juice.Storage.Abstractions;
using Juice.Storage.BackgroundTasks;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IStorageBuilder"/> versions of the upload/download/maintain service collection extensions.
    /// </summary>
    public static class StorageManagerBuilderExtensions
    {
        public static IStorageBuilder AddDefaultUploadManager<T>(this IStorageBuilder builder, Action<UploadOptions> configure)
            where T : class, IFile, new()
        {
            builder.Services.AddDefaultUploadManager<T>(configure);
            return builder;
        }

        public static IStorageBuilder AddDefaultUploadManager<T>(this IStorageBuilder builder, IConfiguration configuration,
            Action<UploadOptions>? configure = default)
            where T : class, IFile, new()
        {
            builder.Services.AddDefaultUploadManager<T>(configuration, configure);
            return builder;
        }

        public static IStorageBuilder AddDefaultDownloadManager<T>(this IStorageBuilder builder, IConfiguration configuration,
            Action<DownloadOptions>? configure = default)
            where T : class, IFile, new()
        {
            builder.Services.AddDefaultDownloadManager<T>(configuration, configure);
            return builder;
        }

        public static IStorageBuilder AddStorageMaintainServices<T>(this IStorageBuilder builder,
            IConfiguration configuration, string[] identities,
            Action<StorageMaintainOptions>? configure = default)
            where T : class, IFile, new()
        {
            builder.Services.AddStorageMaintainServices<T>(configuration, identities, configure);
            return builder;
        }
    }
}
