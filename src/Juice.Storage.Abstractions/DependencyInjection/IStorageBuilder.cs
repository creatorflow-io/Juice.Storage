using Microsoft.Extensions.DependencyInjection;

namespace Juice.Storage.Abstractions
{
    /// <summary>
    /// Configures Juice storage services: providers, upload/download managers, repositories and network connections.
    /// <para>Each storage package adds its own extension methods to this builder.</para>
    /// </summary>
    public interface IStorageBuilder
    {
        IServiceCollection Services { get; }
    }

    internal sealed class StorageBuilder : IStorageBuilder
    {
        public StorageBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }
    }
}
