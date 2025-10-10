using System.Threading;
using System.Threading.Tasks;
using Juice.Storage.Events;
using Juice.MediatR;
using Microsoft.Extensions.Logging;

namespace Juice.Storage.Tests.EventHandlers
{
    internal class FileUploadedEventHandler : INotificationHandler<FileUploadedEvent>
    {
        private ILogger _logger;
        public FileUploadedEventHandler(ILogger<FileUploadedEventHandler> logger)
        {
            _logger = logger;
        }
        public ValueTask Handle(FileUploadedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"File uploaded: {notification.Name}");
            return ValueTask.CompletedTask;
        }
    }
}
