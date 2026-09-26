using System.Net;
using Juice.Storage.Abstractions;

namespace Juice.Storage.Local.Windows
{
    /// <summary>
    /// Connects to network shares with WNetAddConnection2. UNC paths are used directly by System.IO.
    /// </summary>
    public sealed class WindowsNetworkConnectionFactory : INetworkConnectionFactory
    {
        public INetworkConnection Connect(StorageEndpoint endpoint, NetworkCredential credential)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException($"{nameof(WindowsNetworkConnectionFactory)} is only supported on Windows.");
            }

            // BasePath is the network resource to authenticate to (\\server or \\server\share), fallback to the share of the Uri
            string remoteName;
            if (!string.IsNullOrWhiteSpace(endpoint.BasePath))
            {
                remoteName = UncPath.ToWindowsUnc(endpoint.BasePath);
            }
            else if (UncPath.TryParse(endpoint.Uri, out var server, out var share, out _))
            {
                remoteName = $@"\\{server}\{share}";
            }
            else
            {
                throw new ArgumentException($"Storage endpoint '{endpoint.Uri}' is not a network path.", nameof(endpoint));
            }

            return new WindowsNetworkConnection(remoteName, credential);
        }

        public string ResolvePath(string networkPath) => UncPath.ToWindowsUnc(networkPath);
    }
}
