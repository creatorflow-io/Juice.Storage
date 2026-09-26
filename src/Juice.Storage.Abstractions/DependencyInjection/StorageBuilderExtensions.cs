using Juice.Storage.Abstractions;
using Juice.Storage.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class StorageBuilderExtensions
    {
        /// <summary>
        /// Add the storage core services (same as <see cref="StorageServiceCollectionExtensions.AddStorage(IServiceCollection)"/>)
        /// then configure providers, managers and repositories with the <see cref="IStorageBuilder"/>.
        /// <example>
        /// <code>
        /// services.AddStorage(storage => storage
        ///     .AddLocalStorageProviders()
        ///     .AddWindowsNetworkConnection()
        ///     .AddLinuxNetworkConnection()
        ///     .AddInMemoryUploadManager(configuration.GetSection("Juice:Storage"))
        ///     .AddDefaultDownloadManager&lt;UploadFileInfo&gt;(configuration.GetSection("Juice:Storage")));
        /// </code>
        /// </example>
        /// </summary>
        public static IServiceCollection AddStorage(this IServiceCollection services, Action<IStorageBuilder> configure)
        {
            services.AddStorage();
            configure(new StorageBuilder(services));
            return services;
        }

        /// <summary>
        /// Register a storage provider. Registering the same provider type twice has no effect.
        /// </summary>
        public static IStorageBuilder AddStorageProvider<TProvider>(this IStorageBuilder builder)
            where TProvider : class, IStorageProvider
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageProvider, TProvider>());
            return builder;
        }

        /// <summary>
        /// Replace the <see cref="IStorageRepository"/> that provides storage endpoints by identity.
        /// </summary>
        public static IStorageBuilder AddStorageRepository<TRepository>(this IStorageBuilder builder)
            where TRepository : class, IStorageRepository
        {
            builder.Services.Replace(ServiceDescriptor.Scoped<IStorageRepository, TRepository>());
            return builder;
        }

        /// <summary>
        /// Only for testing purposes, it will increase memory usage
        /// </summary>
        [Obsolete("Only for testing purposes, it will increase memory usage", error: false)]
        public static IStorageBuilder AddInMemoryStorageProvider(this IStorageBuilder builder)
        {
            builder.Services.TryAddSingleton<InMemoryStorageProvider>();
            builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IStorageProvider, InMemoryStorageProvider>(
                sp => sp.GetRequiredService<InMemoryStorageProvider>()));
            return builder;
        }
    }
}
