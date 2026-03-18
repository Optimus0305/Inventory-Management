using InventoryHold.Domain.Aggregates;
using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using InventoryHold.Domain.Enums;

namespace InventoryHold.Infrastructure.Persistence;

/// <summary>
/// MongoDB repository for <see cref="InventoryHold"/> aggregates.
///
/// Uses a document model separate from the domain aggregate to prevent
/// the persistence concern from leaking into the domain layer.
/// </summary>
public sealed class MongoHoldRepository : IHoldRepository
{
    private readonly IMongoCollection<HoldDocument> _collection;

    public MongoHoldRepository(IOptions<MongoDbSettings> mongoSettings)
    {
        var client = new MongoClient(mongoSettings.Value.ConnectionString);
        var db = client.GetDatabase(mongoSettings.Value.DatabaseName);
        _collection = db.GetCollection<HoldDocument>("holds");

        EnsureIndexes();
    }

    public async Task<Hold?> GetByIdAsync(string holdId, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(Builders<HoldDocument>.Filter.Eq(d => d.Id, holdId))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : doc.ToDomain();
    }

    public async Task SaveAsync(Hold hold, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(HoldDocument.FromDomain(hold), cancellationToken: ct);
    }

    public async Task UpdateAsync(Hold hold, CancellationToken ct = default)
    {
        var filter = Builders<HoldDocument>.Filter.Eq(d => d.Id, hold.Id);
        var doc = HoldDocument.FromDomain(hold);
        await _collection.ReplaceOneAsync(filter, doc, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<Hold>> GetExpiredHoldsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var filter = Builders<HoldDocument>.Filter.And(
            Builders<HoldDocument>.Filter.Eq(d => d.Status, HoldStatus.Active.ToString()),
            Builders<HoldDocument>.Filter.Lt(d => d.ExpiresAt, now));

        var docs = await _collection.Find(filter).ToListAsync(ct);
        return docs.Select(d => d.ToDomain()).ToList();
    }

    private void EnsureIndexes()
    {
        var statusIdx = new CreateIndexModel<HoldDocument>(
            Builders<HoldDocument>.IndexKeys
                .Ascending(d => d.Status)
                .Ascending(d => d.ExpiresAt),
            new CreateIndexOptions { Name = "idx_status_expires" });

        _collection.Indexes.CreateMany([statusIdx]);
    }

    // ── MongoDB document model ───────────────────────────────────────────────

    private sealed class HoldDocument
    {
        [BsonId]
        public string Id { get; set; } = default!;
        public string ProductId { get; set; } = default!;
        public string CustomerId { get; set; } = default!;
        public int Quantity { get; set; }
        public string Status { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }

        public static HoldDocument FromDomain(Hold h) => new()
        {
            Id = h.Id,
            ProductId = h.ProductId,
            CustomerId = h.CustomerId,
            Quantity = h.Quantity,
            Status = h.Status.ToString(),
            CreatedAt = h.CreatedAt,
            ExpiresAt = h.ExpiresAt,
            ReleasedAt = h.ReleasedAt
        };

        public Hold ToDomain() =>
            InventoryHoldRehydrator.Rehydrate(Id, ProductId, CustomerId, Quantity,
                Enum.Parse<HoldStatus>(Status), CreatedAt, ExpiresAt, ReleasedAt);
    }
}
