using InventoryHold.Domain.Events;

namespace InventoryHold.Domain.Interfaces;

/// <summary>
/// Abstraction over the event publishing pipeline.
/// Implementations MUST NOT use fire-and-forget;
/// failures should be surfaced so the outbox can retry.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes a domain event to the message broker.
    /// The call is awaited; a failure propagates the exception
    /// so the caller (Outbox publisher) can record the failure.
    /// </summary>
    Task PublishAsync(DomainEvent domainEvent, CancellationToken ct = default);
}
