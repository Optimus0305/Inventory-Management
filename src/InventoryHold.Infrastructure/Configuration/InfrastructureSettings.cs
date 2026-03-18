namespace InventoryHold.Infrastructure.Configuration;

/// <summary>
/// RabbitMQ exchange and queue topology constants.
///
/// TOPOLOGY RATIONALE
/// ──────────────────
/// Exchange type: topic
///   Chosen over direct/fanout because topic exchanges let consumers filter by
///   routing-key patterns (e.g. "hold.*"), making it trivial to add new event
///   types without re-configuring existing consumers.
///
/// Dead-letter exchange (DLX):
///   Every queue is backed by a DLX.  Messages that exceed MaxRetries are
///   moved to the corresponding DL-queue instead of being silently dropped.
///   Ops can inspect and replay dead-lettered messages without code changes.
///
/// Per-event queues:
///   Separate queues for each event type so a slow consumer for HoldExpired
///   does not block HoldCreated processing.
/// </summary>
public static class RabbitMqTopology
{
    // ── Main exchange ────────────────────────────────────────────────────────
    public const string MainExchange = "inventory.hold.events";
    public const string ExchangeType = "topic";

    // ── Dead-letter exchange ─────────────────────────────────────────────────
    public const string DeadLetterExchange = "inventory.hold.events.dlx";

    // ── Routing keys ─────────────────────────────────────────────────────────
    public const string HoldCreatedRoutingKey = "hold.created";
    public const string HoldReleasedRoutingKey = "hold.released";
    public const string HoldExpiredRoutingKey = "hold.expired";

    // ── Queues ───────────────────────────────────────────────────────────────
    public const string HoldCreatedQueue = "inventory.hold.created";
    public const string HoldReleasedQueue = "inventory.hold.released";
    public const string HoldExpiredQueue = "inventory.hold.expired";

    // ── Dead-letter queues ───────────────────────────────────────────────────
    public const string HoldCreatedDlq = "inventory.hold.created.dlq";
    public const string HoldReleasedDlq = "inventory.hold.released.dlq";
    public const string HoldExpiredDlq = "inventory.hold.expired.dlq";
}

/// <summary>
/// Strongly-typed settings bound from appsettings.json → "RabbitMq" section.
/// </summary>
public sealed class RabbitMqSettings
{
    public const string SectionName = "RabbitMq";

    public required string Host { get; init; }
    public int Port { get; init; } = 5672;
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string VirtualHost { get; init; }

    /// <summary>Maximum number of consecutive publish attempts before a message is dead-lettered.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Initial retry back-off delay in milliseconds (doubles each attempt).</summary>
    public int InitialRetryDelayMs { get; init; } = 500;
}

/// <summary>
/// Strongly-typed settings for the transactional outbox background worker.
/// </summary>
public sealed class OutboxSettings
{
    public const string SectionName = "Outbox";

    /// <summary>How many messages to fetch and publish per polling cycle.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Polling interval in seconds.</summary>
    public int PollingIntervalSeconds { get; init; } = 5;

    /// <summary>Maximum publish retries before a message is considered poisoned.</summary>
    public int MaxRetries { get; init; } = 5;
}

/// <summary>
/// MongoDB connection settings.
/// </summary>
public sealed class MongoDbSettings
{
    public const string SectionName = "MongoDb";

    public required string ConnectionString { get; init; }
    public required string DatabaseName { get; init; }
}

/// <summary>
/// Hold expiry worker settings.
/// </summary>
public sealed class HoldExpirySettings
{
    public const string SectionName = "HoldExpiry";

    /// <summary>Polling interval in seconds for the hold expiry background worker.</summary>
    public int PollingIntervalSeconds { get; init; } = 30;

    /// <summary>Maximum hold duration clients may request, in seconds.</summary>
    public int MaxHoldDurationSeconds { get; init; } = 3600;

    /// <summary>Default hold duration when the client does not specify one, in seconds.</summary>
    public int DefaultHoldDurationSeconds { get; init; } = 900;
}
