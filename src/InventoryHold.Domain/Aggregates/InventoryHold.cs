using InventoryHold.Domain.Enums;
using InventoryHold.Domain.Events;
using InventoryHold.Domain.Exceptions;

namespace InventoryHold.Domain.Aggregates;

/// <summary>
/// Aggregate root representing a temporary inventory reservation.
///
/// Invariants enforced by this class:
/// - A hold can only be released or expired once (state machine).
/// - All state transitions emit a domain event recorded in _domainEvents.
/// - Callers MUST persist the aggregate AND drain _domainEvents atomically
///   using the Transactional Outbox pattern.
/// </summary>
public sealed class Hold
{
    private readonly List<DomainEvent> _domainEvents = [];

    public string Id { get; private set; } = default!;
    public string ProductId { get; private set; } = default!;
    public string CustomerId { get; private set; } = default!;
    public int Quantity { get; private set; }
    public HoldStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }

    // Required for MongoDB deserialization
    private Hold() { }

    // Used exclusively by InventoryHoldRehydrator — no domain events emitted
    internal Hold(
        string id, string productId, string customerId, int quantity,
        HoldStatus status, DateTimeOffset createdAt, DateTimeOffset expiresAt,
        DateTimeOffset? releasedAt)
    {
        Id = id;
        ProductId = productId;
        CustomerId = customerId;
        Quantity = quantity;
        Status = status;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        ReleasedAt = releasedAt;
    }

    /// <summary>
    /// Factory method — the only way to create a valid hold.
    /// Emits <see cref="HoldCreatedEvent"/>.
    /// </summary>
    public static Hold Create(
        string holdId,
        string productId,
        string customerId,
        int quantity,
        TimeSpan holdDuration)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");
        if (holdDuration <= TimeSpan.Zero)
            throw new DomainException("Hold duration must be positive.");

        var now = DateTimeOffset.UtcNow;
        var hold = new Hold
        {
            Id = holdId,
            ProductId = productId,
            CustomerId = customerId,
            Quantity = quantity,
            Status = HoldStatus.Active,
            CreatedAt = now,
            ExpiresAt = now.Add(holdDuration)
        };

        hold._domainEvents.Add(new HoldCreatedEvent
        {
            HoldId = hold.Id,
            ProductId = hold.ProductId,
            CustomerId = hold.CustomerId,
            Quantity = hold.Quantity,
            ExpiresAt = hold.ExpiresAt
        });

        return hold;
    }

    /// <summary>
    /// Releases the hold with an explicit reason.
    /// Emits <see cref="HoldReleasedEvent"/>.
    /// </summary>
    public void Release(string reason)
    {
        if (Status == HoldStatus.Released)
            throw new HoldAlreadyReleasedException(Id);
        if (Status == HoldStatus.Expired)
            throw new HoldAlreadyExpiredException(Id);

        Status = HoldStatus.Released;
        ReleasedAt = DateTimeOffset.UtcNow;

        _domainEvents.Add(new HoldReleasedEvent
        {
            HoldId = Id,
            ProductId = ProductId,
            CustomerId = CustomerId,
            Quantity = Quantity,
            Reason = reason
        });
    }

    /// <summary>
    /// Marks the hold as expired (called by the background expiry worker).
    /// Emits <see cref="HoldExpiredEvent"/>.
    /// </summary>
    public void Expire()
    {
        if (Status == HoldStatus.Released)
            throw new HoldAlreadyReleasedException(Id);
        if (Status == HoldStatus.Expired)
            throw new HoldAlreadyExpiredException(Id);

        var expiredAt = DateTimeOffset.UtcNow;
        Status = HoldStatus.Expired;
        ReleasedAt = expiredAt;

        _domainEvents.Add(new HoldExpiredEvent
        {
            HoldId = Id,
            ProductId = ProductId,
            CustomerId = CustomerId,
            Quantity = Quantity,
            ExpiredAt = expiredAt
        });
    }

    /// <summary>Returns whether the hold TTL has elapsed.</summary>
    public bool IsExpired(DateTimeOffset? asOf = null) =>
        (asOf ?? DateTimeOffset.UtcNow) >= ExpiresAt;

    /// <summary>
    /// Drains all pending domain events in insertion order.
    /// Called after the aggregate is persisted so events can be written to the outbox.
    /// </summary>
    public IReadOnlyList<DomainEvent> DrainDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
