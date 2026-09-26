using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Juice.Storage.Local.Windows
{
    /// <summary>
    /// A connection to a network resource established by WNetAddConnection2 (mpr.dll).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsNetworkConnection : INetworkConnection
    {
        private readonly string _networkName;
        private bool _disposed;

        public WindowsNetworkConnection(string networkName, NetworkCredential credentials)
        {
            _networkName = networkName;

            var netResource = new NetResource
            {
                Scope = ResourceScope.GlobalNetwork,
                ResourceType = ResourceType.Disk,
                DisplayType = ResourceDisplaytype.Share,
                RemoteName = networkName
            };

            var userName = string.IsNullOrEmpty(credentials.Domain)
                ? credentials.UserName
                : string.Format(@"{0}\{1}", credentials.Domain, credentials.UserName);

            var result = WNetAddConnection2(netResource, credentials.Password, userName, 0);

            if (result != 0)
            {
                throw new Win32Exception(result, $"Error connecting to remote share {networkName} as {userName}: {new Win32Exception(result).Message}");
            }
        }

        public string ResolvePath(string networkPath) => UncPath.ToWindowsUnc(networkPath);

        ~WindowsNetworkConnection()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            WNetCancelConnection2(_networkName, 0, true);
            _disposed = true;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(NetResource netResource,
            string? password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(string name, int flags,
            bool force);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class NetResource
        {
            public ResourceScope Scope;
            public ResourceType ResourceType;
            public ResourceDisplaytype DisplayType;
            public int Usage;
            public string? LocalName;
            public string? RemoteName;
            public string? Comment;
            public string? Provider;
        }

        private enum ResourceScope : int
        {
            Connected = 1,
            GlobalNetwork,
            Remembered,
            Recent,
            Context
        };

        private enum ResourceType : int
        {
            Any = 0,
            Disk = 1,
            Print = 2,
            Reserved = 8,
        }

        private enum ResourceDisplaytype : int
        {
            Generic = 0x0,
            Domain = 0x01,
            Server = 0x02,
            Share = 0x03,
            File = 0x04,
            Group = 0x05,
            Network = 0x06,
            Root = 0x07,
            Shareadmin = 0x08,
            Directory = 0x09,
            Tree = 0x0a,
            Ndscontainer = 0x0b
        }
    }
}
