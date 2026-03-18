import { useState, type FormEvent } from 'react';
import { createHold } from '../api/client';
import type { Hold, InventoryItem } from '../types';

interface Props {
  inventory: InventoryItem[];
  onHoldCreated: (hold: Hold) => void;
  onInventoryRefresh: () => void;
}

export function CreateHoldForm({ inventory, onHoldCreated, onInventoryRefresh }: Props) {
  const [productId, setProductId] = useState('');
  const [quantity, setQuantity] = useState(1);
  const [userId, setUserId] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const availableItems = inventory.filter((i) => i.available > 0);
  const selectedItem = inventory.find((i) => i.productId === productId);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!productId || !userId.trim()) {
      setError('Please fill in all required fields.');
      return;
    }
    if (quantity <= 0 || !Number.isInteger(quantity)) {
      setError('Quantity must be a positive integer.');
      return;
    }

    setLoading(true);
    setError(null);
    setSuccess(null);

    try {
      const hold = await createHold({ productId, quantity, userId: userId.trim() });
      onHoldCreated(hold);
      onInventoryRefresh();
      setSuccess(`Hold created! ID: ${hold.holdId}`);
      setProductId('');
      setQuantity(1);
      setUserId('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create hold');
    } finally {
      setLoading(false);
    }
  };

  return (
    <section className="card">
      <div className="card-header">
        <h2>➕ Create Hold</h2>
      </div>

      {error && <div className="alert alert-error">{error}</div>}
      {success && <div className="alert alert-success">{success}</div>}

      <form onSubmit={handleSubmit} className="form">
        <div className="form-group">
          <label htmlFor="product">Product *</label>
          <select
            id="product"
            value={productId}
            onChange={(e) => setProductId(e.target.value)}
            required
          >
            <option value="">— Select a product —</option>
            {availableItems.map((item) => (
              <option key={item.productId} value={item.productId}>
                {item.name} (available: {item.available})
              </option>
            ))}
          </select>
        </div>

        <div className="form-group">
          <label htmlFor="quantity">Quantity *</label>
          <input
            id="quantity"
            type="number"
            min={1}
            max={selectedItem?.available ?? 9999}
            value={quantity}
            onChange={(e) => setQuantity(parseInt(e.target.value, 10))}
            required
          />
          {selectedItem && (
            <small className="muted">Available: {selectedItem.available}</small>
          )}
        </div>

        <div className="form-group">
          <label htmlFor="userId">User ID *</label>
          <input
            id="userId"
            type="text"
            placeholder="e.g. user-123"
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
            required
          />
        </div>

        <button
          type="submit"
          className="btn btn-primary"
          disabled={loading || !productId || !userId.trim()}
        >
          {loading ? 'Creating…' : 'Place Hold'}
        </button>
      </form>
    </section>
  );
}
