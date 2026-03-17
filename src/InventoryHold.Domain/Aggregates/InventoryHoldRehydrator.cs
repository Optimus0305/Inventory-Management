using InventoryHold.Domain.Enums;

namespace InventoryHold.Domain.Aggregates;

/// <summary>
/// Reconstitutes an <see cref="InventoryHold"/> aggregate from persistence
/// without triggering domain event emission.
///
/// Kept in the Domain layer so the Infrastructure layer never bypasses
/// aggregate invariants; the aggregate exposes only this factory for rehydration.
/// </summary>
public static class InventoryHoldRehydrator
{
    public static Hold Rehydrate(
        string id,
        string productId,
        string customerId,
        int quantity,
        HoldStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? releasedAt)
    {
        return new Hold(
            id, productId, customerId, quantity,
            status, createdAt, expiresAt, releasedAt);
    }
}
