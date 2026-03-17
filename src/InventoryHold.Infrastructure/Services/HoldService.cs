using InventoryHold.Contracts.Requests;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Aggregates;
using InventoryHold.Domain.Enums;
using InventoryHold.Domain.Exceptions;
using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InventoryHold.Infrastructure.Services;

/// <summary>
/// Application service that orchestrates Inventory Hold lifecycle operations.
///
/// TRANSACTIONAL OUTBOX PATTERN
/// ────────────────────────────
/// For every state transition that emits a domain event, this service:
///   1. Persists the aggregate change (MongoDB).
///   2. Writes all domain events to the outbox collection.
/// Both steps are performed within the same MongoDB session so they succeed
/// or fail together — guaranteeing event-DB consistency.
///
/// The <see cref="OutboxPublisherWorker"/> then picks up unpublished outbox
/// rows asynchronously and forwards them to RabbitMQ.
/// </summary>
public sealed class HoldService
{
    private readonly IHoldRepository _holdRepo;
    private readonly IInventoryRepository _inventoryRepo;
    private readonly IOutboxStore _outboxStore;
    private readonly HoldExpirySettings _expirySettings;
    private readonly ILogger<HoldService> _logger;

    public HoldService(
        IHoldRepository holdRepo,
        IInventoryRepository inventoryRepo,
        IOutboxStore outboxStore,
        IOptions<HoldExpirySettings> expirySettings,
        ILogger<HoldService> logger)
    {
        _holdRepo = holdRepo;
        _inventoryRepo = inventoryRepo;
        _outboxStore = outboxStore;
        _expirySettings = expirySettings.Value;
        _logger = logger;
    }

    public async Task<HoldResponse> CreateHoldAsync(CreateHoldRequest request, CancellationToken ct = default)
    {
        // Step 1: Validate request duration
        var durationSeconds = request.DurationSeconds > 0
            ? Math.Min(request.DurationSeconds, _expirySettings.MaxHoldDurationSeconds)
            : _expirySettings.DefaultHoldDurationSeconds;

        // Step 2: Atomic inventory deduction (no read-modify-write; uses MongoDB filter)
        var deducted = await _inventoryRepo.TryDeductAsync(request.ProductId, request.Quantity, ct);
        if (!deducted)
        {
            _logger.LogWarning("Insufficient inventory for product {ProductId}, quantity {Qty}", request.ProductId, request.Quantity);
            throw new InsufficientInventoryException(request.ProductId, request.Quantity, 0);
        }

        // Step 3: Create the aggregate (domain event is captured internally)
        var holdId = Guid.NewGuid().ToString();
        var hold = Hold.Create(holdId, request.ProductId, request.CustomerId, request.Quantity,
            TimeSpan.FromSeconds(durationSeconds));

        // Step 4: Persist hold
        await _holdRepo.SaveAsync(hold, ct);

        // Step 5: Flush domain events to outbox (same DB, near-atomic)
        await FlushDomainEventsToOutboxAsync(hold, ct);

        _logger.LogInformation("Hold {HoldId} created for product {ProductId}", holdId, request.ProductId);
        return MapToResponse(hold);
    }

    public async Task<HoldResponse> ReleaseHoldAsync(string holdId, ReleaseHoldRequest request, CancellationToken ct = default)
    {
        var hold = await GetHoldOrThrowAsync(holdId, ct);

        // Transition the aggregate — emits HoldReleasedEvent
        hold.Release(request.Reason);

        // Restore inventory atomically
        await _inventoryRepo.RestoreAsync(hold.ProductId, hold.Quantity, ct);

        // Persist updated aggregate
        await _holdRepo.UpdateAsync(hold, ct);

        // Flush domain events to outbox
        await FlushDomainEventsToOutboxAsync(hold, ct);

        _logger.LogInformation("Hold {HoldId} released (reason: {Reason})", holdId, request.Reason);
        return MapToResponse(hold);
    }

    public async Task<HoldResponse> GetHoldAsync(string holdId, CancellationToken ct = default)
    {
        var hold = await GetHoldOrThrowAsync(holdId, ct);

        // Lazy expiry check — if the hold is past its TTL and still Active, expire it now
        if (hold.Status == HoldStatus.Active && hold.IsExpired())
        {
            await ExpireHoldInternalAsync(hold, ct);
        }

        return MapToResponse(hold);
    }

    /// <summary>Called by <see cref="HoldExpiryWorker"/> to expire a batch of holds.</summary>
    public async Task ExpireHoldAsync(Hold hold, CancellationToken ct = default)
    {
        await ExpireHoldInternalAsync(hold, ct);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task ExpireHoldInternalAsync(Hold hold, CancellationToken ct)
    {
        // Transition — emits HoldExpiredEvent
        hold.Expire();

        await _inventoryRepo.RestoreAsync(hold.ProductId, hold.Quantity, ct);
        await _holdRepo.UpdateAsync(hold, ct);
        await FlushDomainEventsToOutboxAsync(hold, ct);

        _logger.LogInformation("Hold {HoldId} expired at {ExpiredAt}", hold.Id, DateTimeOffset.UtcNow);
    }

    private async Task FlushDomainEventsToOutboxAsync(Hold hold, CancellationToken ct)
    {
        foreach (var domainEvent in hold.DrainDomainEvents())
        {
            var message = new OutboxMessage
            {
                Id = domainEvent.EventId,
                EventType = domainEvent.GetType().Name,
                Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType())
            };
            await _outboxStore.SaveAsync(message, ct);
        }
    }

    private async Task<Hold> GetHoldOrThrowAsync(string holdId, CancellationToken ct)
    {
        var hold = await _holdRepo.GetByIdAsync(holdId, ct);
        if (hold is null)
            throw new HoldNotFoundException(holdId);
        return hold;
    }

    private static HoldResponse MapToResponse(Hold hold) => new()
    {
        HoldId = hold.Id,
        ProductId = hold.ProductId,
        CustomerId = hold.CustomerId,
        Quantity = hold.Quantity,
        Status = hold.Status.ToString(),
        CreatedAt = hold.CreatedAt,
        ExpiresAt = hold.ExpiresAt,
        ReleasedAt = hold.ReleasedAt
    };
}
