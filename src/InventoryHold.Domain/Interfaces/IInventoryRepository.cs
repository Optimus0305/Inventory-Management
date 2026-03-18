using InventoryHold.Domain.Aggregates;

namespace InventoryHold.Domain.Interfaces;

/// <summary>Repository interface for InventoryItem aggregate persistence.</summary>
public interface IInventoryRepository
{
    Task<InventoryItem?> GetByProductIdAsync(string productId, CancellationToken ct = default);

    /// <summary>
    /// Atomically decrements available quantity by <paramref name="quantity"/>.
    /// Uses MongoDB <c>findOneAndUpdate</c> with a filter requiring
    /// <c>availableQuantity &gt;= quantity</c> to prevent overselling.
    /// Returns <c>false</c> when stock is insufficient (concurrent depletion).
    /// </summary>
    Task<bool> TryDeductAsync(string productId, int quantity, CancellationToken ct = default);

    /// <summary>Atomically restores quantity (hold released or expired).</summary>
    Task RestoreAsync(string productId, int quantity, CancellationToken ct = default);
}
