using Juice.Storage.Abstractions;
using Juice.Storage.Local.Linux;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class LinuxNetworkConnectionStorageBuilderExtensions
    {
        /// <summary>
        /// Mount network shares with mount.cifs to access them with the storage endpoint credentials.
        /// <para>Only registered when running on Linux, so it can be chained with
        /// AddWindowsNetworkConnection() for cross-platform apps.</para>
        /// </summary>
        public static IStorageBuilder AddLinuxNetworkConnection(this IStorageBuilder builder,
            Action<LinuxNetworkConnectionOptions>? configure = null)
        {
            if (OperatingSystem.IsLinux())
            {
                builder.Services.AddLinuxNetworkConnection(configure);
            }
            return builder;
        }
    }
}
