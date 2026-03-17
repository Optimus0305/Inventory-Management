namespace InventoryHold.Contracts.Events;

/// <summary>
/// Wire-format event published to RabbitMQ when a hold is created.
///
/// Field rationale:
/// - EventId      → Consumer idempotency key; store to detect duplicate deliveries.
/// - EventType    → Enables polymorphic deserialization / routing without inspecting payload.
/// - SchemaVersion → Allows non-breaking schema evolution.
/// - OccurredAt   → UTC event time; consumers should use this, not wall-clock time.
/// - HoldId       → Correlation key; links event to the InventoryHold aggregate.
/// - ProductId    → Enables per-product downstream filtering.
/// - CustomerId   → Enables per-customer downstream filtering.
/// - Quantity     → Quantity reserved; needed for inventory restoration on downstream failure.
/// - ExpiresAt    → Consumers can compute remaining TTL without calling back to this service.
/// </summary>
public sealed record HoldCreatedEventDto
{
    public Guid EventId { get; init; }
    public string EventType => "HoldCreated";
    public int SchemaVersion => 1;
    public DateTimeOffset OccurredAt { get; init; }
    public required string HoldId { get; init; }
    public required string ProductId { get; init; }
    public required string CustomerId { get; init; }
    public int Quantity { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Wire-format event published when a hold is explicitly released.
/// </summary>
public sealed record HoldReleasedEventDto
{
    public Guid EventId { get; init; }
    public string EventType => "HoldReleased";
    public int SchemaVersion => 1;
    public DateTimeOffset OccurredAt { get; init; }
    public required string HoldId { get; init; }
    public required string ProductId { get; init; }
    public required string CustomerId { get; init; }
    public int Quantity { get; init; }
    /// <summary>Machine-readable reason code (e.g., "CustomerCancelled", "OrderCompleted").</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Wire-format event published when a hold TTL elapses automatically.
/// </summary>
public sealed record HoldExpiredEventDto
{
    public Guid EventId { get; init; }
    public string EventType => "HoldExpired";
    public int SchemaVersion => 1;
    public DateTimeOffset OccurredAt { get; init; }
    public required string HoldId { get; init; }
    public required string ProductId { get; init; }
    public required string CustomerId { get; init; }
    public int Quantity { get; init; }
    public DateTimeOffset ExpiredAt { get; init; }
}
