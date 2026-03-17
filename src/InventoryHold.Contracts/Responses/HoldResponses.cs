namespace InventoryHold.Contracts.Responses;

public sealed record HoldResponse
{
    public required string HoldId { get; init; }
    public required string ProductId { get; init; }
    public required string CustomerId { get; init; }
    public int Quantity { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ReleasedAt { get; init; }
}

public sealed record ErrorResponse
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
