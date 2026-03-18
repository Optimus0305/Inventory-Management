using InventoryHold.Domain.Events;
using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InventoryHold.Infrastructure.Outbox;

/// <summary>
/// Background worker that implements the Transactional Outbox Pattern.
///
/// ALGORITHM
/// ─────────
/// 1. Poll MongoDB for unpublished outbox messages in insertion order.
/// 2. For each message, deserialize the domain event and call IEventPublisher.
/// 3. On success: mark the outbox row as published (idempotent update).
/// 4. On failure: increment RetryCount + store error; skip until next cycle.
///    After MaxRetries failures the message remains in the outbox and must be
///    inspected manually or by an alerting pipeline.
///
/// CONSISTENCY GUARANTEE
/// ─────────────────────
/// Because the outbox row was written in the same MongoDB transaction as the
/// aggregate, we can guarantee:
///   • DB succeeds, event publish fails → row remains, worker will retry.
///   • Event publishes but DB fails     → impossible; the transaction that wrote
///     the outbox row is rolled back, so no outbox row exists to publish from.
///
/// IDEMPOTENCY
/// ───────────
/// If the worker crashes after publishing but before calling MarkPublishedAsync,
/// the message will be re-published on the next cycle.  Consumers MUST treat
/// EventId as an idempotency key and ignore events they have already processed.
/// </summary>
public sealed class OutboxPublisherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxSettings _settings;
    private readonly ILogger<OutboxPublisherWorker> _logger;

    private static readonly Dictionary<string, Type> EventTypeMap = new()
    {
        { nameof(HoldCreatedEvent),  typeof(HoldCreatedEvent) },
        { nameof(HoldReleasedEvent), typeof(HoldReleasedEvent) },
        { nameof(HoldExpiredEvent),  typeof(HoldExpiredEvent) }
    };

    public OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxSettings> settings,
        ILogger<OutboxPublisherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxPublisherWorker started (polling every {Interval}s)", _settings.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in OutboxPublisherWorker cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var messages = await outboxStore.GetUnpublishedAsync(_settings.BatchSize, ct);

        if (messages.Count == 0)
            return;

        _logger.LogDebug("Processing {Count} outbox messages", messages.Count);

        foreach (var message in messages)
        {
            if (message.RetryCount >= _settings.MaxRetries)
            {
                _logger.LogWarning(
                    "Outbox message {MessageId} ({EventType}) has exceeded MaxRetries ({MaxRetries}). Skipping.",
                    message.Id, message.EventType, _settings.MaxRetries);
                continue;
            }

            try
            {
                var domainEvent = DeserializeEvent(message.EventType, message.Payload);

                if (domainEvent is null)
                {
                    _logger.LogError("Cannot deserialize event type '{EventType}' for message {MessageId}", message.EventType, message.Id);
                    await outboxStore.RecordFailureAsync(message.Id, $"Unknown event type: {message.EventType}", ct);
                    continue;
                }

                await publisher.PublishAsync(domainEvent, ct);
                await outboxStore.MarkPublishedAsync(message.Id, ct);

                _logger.LogInformation(
                    "Outbox message {MessageId} ({EventType}) published successfully",
                    message.Id, message.EventType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to publish outbox message {MessageId} ({EventType}), attempt {Retry}/{Max}",
                    message.Id, message.EventType, message.RetryCount + 1, _settings.MaxRetries);

                await outboxStore.RecordFailureAsync(message.Id, ex.Message, ct);
            }
        }
    }

    private static DomainEvent? DeserializeEvent(string eventType, string payload)
    {
        if (!EventTypeMap.TryGetValue(eventType, out var type))
            return null;

        return (DomainEvent?)JsonSerializer.Deserialize(payload, type);
    }
}
