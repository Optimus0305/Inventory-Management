using InventoryHold.Domain.Aggregates;

namespace InventoryHold.Domain.Interfaces;

/// <summary>Repository interface for InventoryHold aggregate persistence.</summary>
public interface IHoldRepository
{
    Task<Hold?> GetByIdAsync(string holdId, CancellationToken ct = default);

    /// <summary>Persists a new hold. Must be called within an outbox-aware unit of work.</summary>
    Task SaveAsync(Hold hold, CancellationToken ct = default);

    /// <summary>Persists updated hold state (release or expire).</summary>
    Task UpdateAsync(Hold hold, CancellationToken ct = default);

    /// <summary>Returns Active holds whose ExpiresAt has elapsed.</summary>
    Task<IReadOnlyList<Hold>> GetExpiredHoldsAsync(CancellationToken ct = default);
}
