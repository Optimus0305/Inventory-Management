using InventoryHold.Domain.Aggregates;
using InventoryHold.Domain.Interfaces;
using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Persistence;

/// <summary>
/// MongoDB repository for <see cref="InventoryItem"/> aggregates.
///
/// Atomic inventory deduction uses a filtered <c>findOneAndUpdate</c> so that
/// concurrent requests cannot both succeed when only enough stock exists for one.
/// This eliminates the read-modify-write race condition.
/// </summary>
public sealed class MongoInventoryRepository : IInventoryRepository
{
    private readonly IMongoCollection<InventoryDocument> _collection;

    public MongoInventoryRepository(IOptions<MongoDbSettings> mongoSettings)
    {
        var client = new MongoClient(mongoSettings.Value.ConnectionString);
        var db = client.GetDatabase(mongoSettings.Value.DatabaseName);
        _collection = db.GetCollection<InventoryDocument>("inventory_items");

        EnsureIndexes();
    }

    public async Task<InventoryItem?> GetByProductIdAsync(string productId, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(Builders<InventoryDocument>.Filter.Eq(d => d.ProductId, productId))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : doc.ToDomain();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The filter <c>{ productId, reservedQty + quantity &lt;= totalQty }</c>
    /// ensures atomicity: only one concurrent request can win when stock is tight.
    /// Returns false if the document was not updated (insufficient stock).
    /// </remarks>
    public async Task<bool> TryDeductAsync(string productId, int quantity, CancellationToken ct = default)
    {
        // Filter: product exists AND has enough available stock
        var filter = Builders<InventoryDocument>.Filter.And(
            Builders<InventoryDocument>.Filter.Eq(d => d.ProductId, productId),
            Builders<InventoryDocument>.Filter.Where(d =>
                d.TotalQuantity - d.ReservedQuantity >= quantity));

        var update = Builders<InventoryDocument>.Update.Inc(d => d.ReservedQuantity, quantity);

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(string productId, int quantity, CancellationToken ct = default)
    {
        var filter = Builders<InventoryDocument>.Filter.Eq(d => d.ProductId, productId);
        var update = Builders<InventoryDocument>.Update.Inc(d => d.ReservedQuantity, -quantity);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    private void EnsureIndexes()
    {
        var productIdx = new CreateIndexModel<InventoryDocument>(
            Builders<InventoryDocument>.IndexKeys.Ascending(d => d.ProductId),
            new CreateIndexOptions { Unique = true, Name = "idx_productId_unique" });

        _collection.Indexes.CreateMany([productIdx]);
    }

    // ── MongoDB document model ───────────────────────────────────────────────

    private sealed class InventoryDocument
    {
        [BsonId]
        public string Id { get; set; } = default!;
        public string ProductId { get; set; } = default!;
        public string ProductName { get; set; } = default!;
        public int TotalQuantity { get; set; }
        public int ReservedQuantity { get; set; }

        public InventoryItem ToDomain() =>
            InventoryItem.Create(Id, ProductId, ProductName, TotalQuantity);
    }
}
