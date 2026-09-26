using System.Net;
using Juice.Storage.Abstractions;

namespace Juice.Storage.Local
{
    /// <summary>
    /// Platform specific access to network shares (SMB/CIFS) so they can be used through <see cref="System.IO"/> APIs.
    /// <para>Implementations: Juice.Storage.Local.Windows, Juice.Storage.Local.Linux.</para>
    /// </summary>
    public interface INetworkConnectionFactory
    {
        /// <summary>
        /// Authenticates to the share that contains <paramref name="endpoint"/> Uri with <paramref name="credential"/>.
        /// </summary>
        INetworkConnection Connect(StorageEndpoint endpoint, NetworkCredential credential);

        /// <summary>
        /// Maps a network path (\\server\share\dir, smb://server/share/dir) to a path that is accessible
        /// with the process identity, without authenticating.
        /// </summary>
        string ResolvePath(string networkPath);
    }

    /// <summary>
    /// An established connection to a network share. Disposing it releases the connection.
    /// </summary>
    public interface INetworkConnection : IDisposable
    {
        /// <summary>
        /// Maps a network path under the connected share to a path accessible through <see cref="System.IO"/> APIs.
        /// </summary>
        string ResolvePath(string networkPath);
    }
}
