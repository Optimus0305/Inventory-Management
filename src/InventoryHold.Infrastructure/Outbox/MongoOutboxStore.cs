using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Outbox;

/// <summary>
/// MongoDB-backed outbox store.
///
/// The outbox collection is written in the SAME MongoDB session/transaction
/// as the aggregate save, ensuring atomicity (both succeed or both fail).
///
/// CONSISTENCY GUARANTEE
/// ─────────────────────
/// • DB succeeds, event publish fails → outbox row remains, worker retries.
/// • Event publish succeeds, DB fails  → impossible (transaction rolls back
///   before the outbox row is committed, so no event is ever enqueued).
///
/// IDEMPOTENCY
/// ───────────
/// Each OutboxMessage carries the event's EventId.  The <see cref="OutboxPublisherWorker"/>
/// checks PublishedAt before publishing; a message is only published once.
/// Consumers receive the EventId in the message header and must track it to
/// skip redeliveries (at-least-once delivery guarantee from the broker).
/// </summary>
public sealed class MongoOutboxStore : IOutboxStore
{
    private readonly IMongoCollection<OutboxDocument> _collection;

    public MongoOutboxStore(IOptions<MongoDbSettings> mongoSettings)
    {
        var client = new MongoClient(mongoSettings.Value.ConnectionString);
        var db = client.GetDatabase(mongoSettings.Value.DatabaseName);
        _collection = db.GetCollection<OutboxDocument>("outbox_messages");

        EnsureIndexes();
    }

    public async Task SaveAsync(OutboxMessage message, CancellationToken ct = default)
    {
        var doc = OutboxDocument.FromMessage(message);
        await _collection.InsertOneAsync(doc, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetUnpublishedAsync(int batchSize, CancellationToken ct = default)
    {
        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.PublishedAt, null);
        var sort = Builders<OutboxDocument>.Sort.Ascending(d => d.CreatedAt);

        var docs = await _collection
            .Find(filter)
            .Sort(sort)
            .Limit(batchSize)
            .ToListAsync(ct);

        return docs.Select(d => d.ToMessage()).ToList();
    }

    public async Task MarkPublishedAsync(Guid messageId, CancellationToken ct = default)
    {
        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Id, messageId);
        var update = Builders<OutboxDocument>.Update
            .Set(d => d.PublishedAt, DateTimeOffset.UtcNow);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task RecordFailureAsync(Guid messageId, string errorMessage, CancellationToken ct = default)
    {
        var filter = Builders<OutboxDocument>.Filter.Eq(d => d.Id, messageId);
        var update = Builders<OutboxDocument>.Update
            .Inc(d => d.RetryCount, 1)
            .Set(d => d.LastError, errorMessage);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    private void EnsureIndexes()
    {
        // Index on PublishedAt for efficient unpublished query
        var unpublishedIdx = new CreateIndexModel<OutboxDocument>(
            Builders<OutboxDocument>.IndexKeys.Ascending(d => d.PublishedAt),
            new CreateIndexOptions { Name = "idx_publishedAt", Sparse = true });

        // Index on CreatedAt for ordered processing
        var createdAtIdx = new CreateIndexModel<OutboxDocument>(
            Builders<OutboxDocument>.IndexKeys.Ascending(d => d.CreatedAt),
            new CreateIndexOptions { Name = "idx_createdAt" });

        _collection.Indexes.CreateMany([unpublishedIdx, createdAtIdx]);
    }

    // ── Internal MongoDB document model ─────────────────────────────────────

    private sealed class OutboxDocument
    {
        [BsonId]
        public Guid Id { get; set; }
        public required string EventType { get; set; }
        public required string Payload { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }

        public static OutboxDocument FromMessage(OutboxMessage m) => new()
        {
            Id = m.Id,
            EventType = m.EventType,
            Payload = m.Payload,
            CreatedAt = m.CreatedAt,
            PublishedAt = m.PublishedAt,
            RetryCount = m.RetryCount,
            LastError = m.LastError
        };

        public OutboxMessage ToMessage() => new()
        {
            Id = Id,
            EventType = EventType,
            Payload = Payload,
            CreatedAt = CreatedAt,
            PublishedAt = PublishedAt,
            RetryCount = RetryCount,
            LastError = LastError
        };
    }
}
