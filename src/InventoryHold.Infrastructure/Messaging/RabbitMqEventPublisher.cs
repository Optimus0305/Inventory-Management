using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Events;
using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Text;
using System.Text.Json;

namespace InventoryHold.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ implementation of <see cref="IEventPublisher"/>.
///
/// DESIGN DECISIONS
/// ────────────────
/// 1. NOT fire-and-forget — PublishAsync is awaited; failures propagate to the Outbox worker.
/// 2. Polly exponential back-off retry (configured via RabbitMqSettings) handles transient
///    broker unavailability without blocking the main thread indefinitely.
/// 3. Publisher confirms via CreateChannelOptions (RabbitMQ.Client v7 API) ensure the broker
///    has accepted the message before we return success to the Outbox.
/// 4. Channel is created per-publish to avoid concurrency issues.
/// 5. Exchange + queue topology is declared idempotent (durable = true, autoDelete = false)
///    so the publisher can restart safely without manual broker configuration.
/// </summary>
public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    private IConnection? _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    // Channel options with publisher confirmations enabled (RabbitMQ.Client v7 API)
    private static readonly CreateChannelOptions ConfirmChannelOptions =
        new(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

    public RabbitMqEventPublisher(
        IOptions<RabbitMqSettings> settings,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _retryPolicy = BuildRetryPolicy();
    }

    /// <inheritdoc/>
    public async Task PublishAsync(DomainEvent domainEvent, CancellationToken ct = default)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            var connection = await GetOrCreateConnectionAsync(ct);

            // Publisher confirms are enabled at channel creation level in RabbitMQ.Client v7
            await using var channel = await connection.CreateChannelAsync(
                ConfirmChannelOptions, ct);

            await DeclareTopologyAsync(channel, ct);

            var (routingKey, payload) = SerializeEvent(domainEvent);

            var props = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = domainEvent.EventId.ToString(),
                Timestamp = new AmqpTimestamp(domainEvent.OccurredAt.ToUnixTimeSeconds()),
                Type = domainEvent.GetType().Name
            };

            var body = Encoding.UTF8.GetBytes(payload);

            // When PublisherConfirmationTrackingEnabled = true, BasicPublishAsync
            // automatically awaits broker ACK before returning.
            await channel.BasicPublishAsync(
                exchange: RabbitMqTopology.MainExchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: props,
                body: body,
                cancellationToken: ct);

            _logger.LogInformation(
                "Published {EventType} (EventId={EventId}) with routing key '{RoutingKey}'",
                domainEvent.GetType().Name, domainEvent.EventId, routingKey);
        });
    }

    // ── Topology declaration ─────────────────────────────────────────────────

    private static async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        // Dead-letter exchange (DLX)
        await channel.ExchangeDeclareAsync(
            exchange: RabbitMqTopology.DeadLetterExchange,
            type: "direct",
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        // Main topic exchange
        await channel.ExchangeDeclareAsync(
            exchange: RabbitMqTopology.MainExchange,
            type: RabbitMqTopology.ExchangeType,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        // Declare each queue with its DLX + DLQ
        await DeclareQueueWithDlqAsync(
            channel,
            RabbitMqTopology.HoldCreatedQueue,
            RabbitMqTopology.HoldCreatedDlq,
            RabbitMqTopology.HoldCreatedRoutingKey,
            ct);

        await DeclareQueueWithDlqAsync(
            channel,
            RabbitMqTopology.HoldReleasedQueue,
            RabbitMqTopology.HoldReleasedDlq,
            RabbitMqTopology.HoldReleasedRoutingKey,
            ct);

        await DeclareQueueWithDlqAsync(
            channel,
            RabbitMqTopology.HoldExpiredQueue,
            RabbitMqTopology.HoldExpiredDlq,
            RabbitMqTopology.HoldExpiredRoutingKey,
            ct);
    }

    private static async Task DeclareQueueWithDlqAsync(
        IChannel channel,
        string queueName,
        string dlqName,
        string routingKey,
        CancellationToken ct)
    {
        // DLQ — plain durable queue, bound to DLX
        await channel.QueueDeclareAsync(
            queue: dlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: dlqName,
            exchange: RabbitMqTopology.DeadLetterExchange,
            routingKey: queueName,
            cancellationToken: ct);

        // Main queue with x-dead-letter-exchange pointing at DLX
        var args = new Dictionary<string, object?>
        {
            { "x-dead-letter-exchange", RabbitMqTopology.DeadLetterExchange },
            { "x-dead-letter-routing-key", queueName }
        };

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: args,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: RabbitMqTopology.MainExchange,
            routingKey: routingKey,
            cancellationToken: ct);
    }

    // ── Event serialization ──────────────────────────────────────────────────

    private static (string routingKey, string payload) SerializeEvent(DomainEvent domainEvent)
    {
        return domainEvent switch
        {
            HoldCreatedEvent e => (
                RabbitMqTopology.HoldCreatedRoutingKey,
                JsonSerializer.Serialize(new HoldCreatedEventDto
                {
                    EventId = e.EventId,
                    OccurredAt = e.OccurredAt,
                    HoldId = e.HoldId,
                    ProductId = e.ProductId,
                    CustomerId = e.CustomerId,
                    Quantity = e.Quantity,
                    ExpiresAt = e.ExpiresAt
                })),

            HoldReleasedEvent e => (
                RabbitMqTopology.HoldReleasedRoutingKey,
                JsonSerializer.Serialize(new HoldReleasedEventDto
                {
                    EventId = e.EventId,
                    OccurredAt = e.OccurredAt,
                    HoldId = e.HoldId,
                    ProductId = e.ProductId,
                    CustomerId = e.CustomerId,
                    Quantity = e.Quantity,
                    Reason = e.Reason
                })),

            HoldExpiredEvent e => (
                RabbitMqTopology.HoldExpiredRoutingKey,
                JsonSerializer.Serialize(new HoldExpiredEventDto
                {
                    EventId = e.EventId,
                    OccurredAt = e.OccurredAt,
                    HoldId = e.HoldId,
                    ProductId = e.ProductId,
                    CustomerId = e.CustomerId,
                    Quantity = e.Quantity,
                    ExpiredAt = e.ExpiredAt
                })),

            _ => throw new InvalidOperationException(
                $"Unknown domain event type: {domainEvent.GetType().Name}")
        };
    }

    // ── Connection management ────────────────────────────────────────────────

    private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            var factory = new ConnectionFactory
            {
                HostName = _settings.Host,
                Port = _settings.Port,
                UserName = _settings.Username,
                Password = _settings.Password,
                VirtualHost = _settings.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken: ct);
            _logger.LogInformation("RabbitMQ connection established to {Host}:{Port}", _settings.Host, _settings.Port);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    // ── Retry policy ─────────────────────────────────────────────────────────

    private AsyncRetryPolicy BuildRetryPolicy()
    {
        return Policy
            .Handle<BrokerUnreachableException>()
            .Or<AlreadyClosedException>()
            .Or<OperationInterruptedException>()
            .Or<IOException>()
            .WaitAndRetryAsync(
                retryCount: _settings.MaxRetries,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromMilliseconds(_settings.InitialRetryDelayMs * Math.Pow(2, attempt - 1)),
                onRetry: (exception, delay, attempt, _) =>
                    _logger.LogWarning(
                        exception,
                        "Publish attempt {Attempt}/{Max} failed. Retrying in {Delay}ms",
                        attempt, _settings.MaxRetries, delay.TotalMilliseconds));
    }

    // ── Disposal ─────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
        _connectionLock.Dispose();
    }
}
