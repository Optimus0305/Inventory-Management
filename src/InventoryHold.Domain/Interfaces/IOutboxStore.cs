namespace InventoryHold.Domain.Interfaces;

/// <summary>
/// Abstraction over the outbox store.
/// Decouples the domain service from the infrastructure persistence mechanism.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Persists a serialised event to the outbox collection as part of the
    /// same MongoDB session/transaction that saves the aggregate.
    /// </summary>
    Task SaveAsync(OutboxMessage message, CancellationToken ct = default);

    /// <summary>Returns up to <paramref name="batchSize"/> unpublished outbox messages.</summary>
    Task<IReadOnlyList<OutboxMessage>> GetUnpublishedAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Marks a message as successfully published.</summary>
    Task MarkPublishedAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>Records a failed publish attempt so the outbox worker can apply back-off.</summary>
    Task RecordFailureAsync(Guid messageId, string errorMessage, CancellationToken ct = default);
}

/// <summary>
/// A serialised envelope stored in MongoDB's outbox collection.
/// Provides the Transactional Outbox pattern's persistence contract.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
