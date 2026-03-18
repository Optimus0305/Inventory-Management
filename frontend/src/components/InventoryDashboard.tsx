import type { InventoryItem } from '../types';

interface Props {
  items: InventoryItem[];
  loading: boolean;
  error: string | null;
  onRefresh: () => void;
}

export function InventoryDashboard({ items, loading, error, onRefresh }: Props) {
  return (
    <section className="card">
      <div className="card-header">
        <h2>📦 Inventory</h2>
        <button className="btn btn-secondary" onClick={onRefresh} disabled={loading}>
          {loading ? '⟳ Loading…' : '⟳ Refresh'}
        </button>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {loading && items.length === 0 ? (
        <p className="muted">Loading inventory…</p>
      ) : items.length === 0 ? (
        <p className="muted">No inventory items found.</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Product</th>
              <th>ID</th>
              <th className="text-right">Total</th>
              <th className="text-right">Reserved</th>
              <th className="text-right">Available</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.productId}>
                <td><strong>{item.name}</strong></td>
                <td className="muted">{item.productId}</td>
                <td className="text-right">{item.quantity}</td>
                <td className="text-right">{item.reserved}</td>
                <td className={`text-right ${item.available === 0 ? 'text-danger' : 'text-success'}`}>
                  <strong>{item.available}</strong>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
