namespace InventoryHold.Contracts.Requests;

public sealed record CreateHoldRequest
{
    public required string ProductId { get; init; }
    public required string CustomerId { get; init; }
    public int Quantity { get; init; }
    /// <summary>Requested hold duration in seconds. Capped by server-side maximum.</summary>
    public int DurationSeconds { get; init; }
}

public sealed record ReleaseHoldRequest
{
    /// <summary>Machine-readable reason code for the release.</summary>
    public required string Reason { get; init; }
}
