using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Juice.Storage;
using Juice.Storage.BackgroundTasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class StorageServiceCollectionExtensions
    {
        public static IServiceCollection AddDefaultUploadManager<T>(this IServiceCollection services, Action<UploadOptions> configure)
           where T : class, IFile, new()
        {
            services.Configure(configure);
            services.AddScoped<IUploadManager, DefaultUploadManager<T>>();
            return services;
        }

        public static IServiceCollection AddDefaultUploadManager<T>(this IServiceCollection services, IConfiguration configuration,
            Action<UploadOptions>? configure = default)
            where T : class, IFile, new()
        {
            services.Configure<UploadOptions>(configuration);
            if (configure != null)
            {
                services.Configure(configure);
            }
            services.AddScoped<IUploadManager, DefaultUploadManager<T>>();
            return services;
        }

        public static IServiceCollection AddDefaultDownloadManager<T>(this IServiceCollection services, IConfiguration configuration,
            Action<DownloadOptions>? configure = default)
            where T : class, IFile, new()
        {
            services.Configure<DownloadOptions>(configuration);
            if (configure != null)
            {
                services.Configure(configure);
            }
            services.AddScoped<IDownloadManager, DefaultDownloadManager<T>>();
            return services;
        }

        public static IServiceCollection AddStorageMaintainServices<T>(this IServiceCollection services,
            IConfiguration configuration, string[] identities,
            Action<StorageMaintainOptions>? configure = default)
            where T : class, IFile, new()
        {
            services.Configure<StorageMaintainOptions>(configuration);
            if (configure != null)
            {
                services.Configure(configure);
            }
            foreach (var identity in identities)
            {
                services.AddTransient<IHostedService>(sp =>
                {
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
                    return new CleanupTimedoutUploadService<T>(loggerFactory, scopeFactory, identity);
                });
            }

            return services;
        }
    }
}
