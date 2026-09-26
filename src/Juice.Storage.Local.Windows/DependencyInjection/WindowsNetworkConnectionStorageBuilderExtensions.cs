using Juice.Storage.Abstractions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class WindowsNetworkConnectionStorageBuilderExtensions
    {
        /// <summary>
        /// Use WNetAddConnection2 to access network shares with the storage endpoint credentials.
        /// <para>Only registered when running on Windows, so it can be chained with
        /// AddLinuxNetworkConnection() for cross-platform apps.</para>
        /// </summary>
        public static IStorageBuilder AddWindowsNetworkConnection(this IStorageBuilder builder)
        {
            if (OperatingSystem.IsWindows())
            {
                builder.Services.AddWindowsNetworkConnection();
            }
            return builder;
        }
    }
}
