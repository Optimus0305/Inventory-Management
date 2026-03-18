using InventoryHold.Domain.Exceptions;

namespace InventoryHold.Domain.Aggregates;

/// <summary>
/// Aggregate root representing a product's inventory levels.
///
/// Atomic deduction is enforced at the database level via MongoDB
/// filtered findOneAndUpdate — this class models the invariants.
/// </summary>
public sealed class InventoryItem
{
    public string Id { get; private set; } = default!;
    public string ProductId { get; private set; } = default!;
    public string ProductName { get; private set; } = default!;
    public int TotalQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }

    public int AvailableQuantity => TotalQuantity - ReservedQuantity;

    // Required for MongoDB deserialization
    private InventoryItem() { }

    public static InventoryItem Create(string id, string productId, string productName, int totalQuantity)
    {
        if (totalQuantity < 0)
            throw new DomainException("Total quantity cannot be negative.");

        return new InventoryItem
        {
            Id = id,
            ProductId = productId,
            ProductName = productName,
            TotalQuantity = totalQuantity,
            ReservedQuantity = 0
        };
    }

    /// <summary>
    /// In-memory guard — the real atomic check is the MongoDB filter condition
    /// <c>{ availableQuantity: { $gte: quantity } }</c>.
    /// </summary>
    public void EnsureSufficientStock(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Requested quantity must be positive.");
        if (AvailableQuantity < quantity)
            throw new InsufficientInventoryException(ProductId, quantity, AvailableQuantity);
    }
}
