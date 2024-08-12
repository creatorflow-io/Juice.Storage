using MediatR;

namespace Juice.Storage.Events
{
    public record FileUploadResumedEvent : INotification
    {
        public FileUploadResumedEvent
            (Guid id, string name, long position, string? correlationId = default, string? userName = null)
        {
            Id = id;
            Name = name;
            CorrelationId = correlationId;
            UserName = userName;
            Position = position;
        }
        public Guid Id { get; }
        public string Name { get; }
        public string? CorrelationId { get; }
        public string? UserName { get; init; }
        public long Position { get; init; }
    }
}
