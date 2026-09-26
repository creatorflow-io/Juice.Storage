using Juice.Storage.Local;
using Juice.Storage.Local.Windows;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class WindowsNetworkConnectionServiceCollectionExtensions
    {
        /// <summary>
        /// Use WNetAddConnection2 to access network shares with the storage endpoint credentials.
        /// </summary>
        public static IServiceCollection AddWindowsNetworkConnection(this IServiceCollection services)
        {
            services.TryAddSingleton<INetworkConnectionFactory, WindowsNetworkConnectionFactory>();
            return services;
        }
    }
}
