using Juice.Storage.Abstractions;
using Juice.Storage.Local;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class LocalStorageBuilderExtensions
    {
        /// <summary>
        /// Register <see cref="LocalStorageProvider"/> for LocalDisk, SMB and VirtualDirectory endpoints.
        /// </summary>
        public static IStorageBuilder AddLocalDiskProvider(this IStorageBuilder builder)
            => builder.AddStorageProvider<LocalStorageProvider>();

        /// <summary>
        /// Register <see cref="FTPStorageProvider"/> for FTP/FTPS endpoints.
        /// </summary>
        public static IStorageBuilder AddFtpProvider(this IStorageBuilder builder)
            => builder.AddStorageProvider<FTPStorageProvider>();

        /// <summary>
        /// Register both <see cref="LocalStorageProvider"/> and <see cref="FTPStorageProvider"/>.
        /// </summary>
        public static IStorageBuilder AddLocalStorageProviders(this IStorageBuilder builder)
            => builder.AddLocalDiskProvider().AddFtpProvider();
    }
}
