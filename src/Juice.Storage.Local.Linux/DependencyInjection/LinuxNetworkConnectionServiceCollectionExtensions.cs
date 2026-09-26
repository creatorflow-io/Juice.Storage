using Juice.Storage.Local;
using Juice.Storage.Local.Linux;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class LinuxNetworkConnectionServiceCollectionExtensions
    {
        /// <summary>
        /// Mount network shares with mount.cifs to access them with the storage endpoint credentials.
        /// </summary>
        public static IServiceCollection AddLinuxNetworkConnection(this IServiceCollection services,
            Action<LinuxNetworkConnectionOptions>? configure = null)
        {
            var builder = services.AddOptions<LinuxNetworkConnectionOptions>();
            if (configure != null)
            {
                builder.Configure(configure);
            }
            services.TryAddSingleton<INetworkConnectionFactory, LinuxNetworkConnectionFactory>();
            return services;
        }
    }
}
