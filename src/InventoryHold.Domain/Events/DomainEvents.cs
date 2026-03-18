namespace InventoryHold.Domain.Events;

/// <summary>
/// Base class for all domain events raised within the InventoryHold aggregate.
/// Every event carries a unique EventId for idempotency, a causal HoldId,
/// and an OccurredAt timestamp so consumers can reason about ordering.
/// </summary>
public abstract record DomainEvent
{
    /// <summary>Globally unique identifier for this specific event occurrence.
    /// Consumers persist this value to detect and skip duplicates.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>UTC timestamp at which the event occurred inside the domain.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Raised when a new inventory hold is created successfully.
/// Downstream consumers (e.g., billing, analytics) subscribe to react.
/// </summary>
public sealed record HoldCreatedEvent : DomainEvent
{
    public required string HoldId { get; init; }
    public required string ProductId { get; init; }
    public required string CustomerId { get; init; }
    public required int Quantity { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Raised when a hold is explicitly released by the customer or system.
/// Inventory is restored; downstream systems should cancel any reservations.
/// </summary>
public sealed record HoldReleasedEvent : DomainEvent
{
    public required string HoldId { get; init; }
    public required string ProductId { get; init; }
    public required string CustomerId { get; init; }
    public required int Quantity { get; init; }
    /// <summary>Human-readable reason for the release (e.g., "CustomerCancelled", "OrderCompleted").</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Raised when a hold TTL elapses without being explicitly released.
/// Inventory is automatically restored; billing must not charge the customer.
/// </summary>
public sealed record HoldExpiredEvent : DomainEvent
{
    public required string HoldId { get; init; }
    public required string ProductId { get; init; }
    public required string CustomerId { get; init; }
    public required int Quantity { get; init; }
    public required DateTimeOffset ExpiredAt { get; init; }
}
