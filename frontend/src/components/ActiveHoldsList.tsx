import { useState, useEffect } from 'react';
import { releaseHold } from '../api/client';
import type { Hold } from '../types';

interface Props {
  holds: Hold[];
  onHoldUpdated: (hold: Hold) => void;
  onInventoryRefresh: () => void;
}

function formatTimeRemaining(expiresAt: string): string {
  const remaining = new Date(expiresAt).getTime() - Date.now();
  if (remaining <= 0) return 'Expired';
  const mins = Math.floor(remaining / 60000);
  const secs = Math.floor((remaining % 60000) / 1000);
  return `${mins}m ${secs}s`;
}

function HoldRow({
  hold,
  onRelease,
}: {
  hold: Hold;
  onRelease: (holdId: string) => void;
}) {
  const [timeRemaining, setTimeRemaining] = useState(() => formatTimeRemaining(hold.expiresAt));
  const [releasing, setReleasing] = useState(false);

  // Update timer every second
  useEffect(() => {
    if (hold.status !== 'active') return;
    const interval = setInterval(() => {
      setTimeRemaining(formatTimeRemaining(hold.expiresAt));
    }, 1000);
    return () => clearInterval(interval);
  }, [hold.expiresAt, hold.status]);

  const handleRelease = async () => {
    if (!confirm(`Release hold ${hold.holdId}? This will restore the reserved inventory.`)) return;
    setReleasing(true);
    try {
      await onRelease(hold.holdId);
    } finally {
      setReleasing(false);
    }
  };

  const statusBadge = {
    active: <span className="badge badge-active">Active</span>,
    released: <span className="badge badge-released">Released</span>,
    expired: <span className="badge badge-expired">Expired</span>,
  }[hold.status];

  return (
    <tr>
      <td className="muted" title={hold.holdId}>
        {hold.holdId.substring(0, 8)}…
      </td>
      <td>{hold.productId}</td>
      <td className="text-right">{hold.quantity}</td>
      <td>{hold.userId}</td>
      <td>{statusBadge}</td>
      <td className={timeRemaining === 'Expired' ? 'text-danger' : ''}>
        {hold.status === 'active' ? timeRemaining : '—'}
      </td>
      <td>
        {hold.status === 'active' && (
          <button
            className="btn btn-danger btn-sm"
            onClick={handleRelease}
            disabled={releasing}
          >
            {releasing ? '…' : 'Release'}
          </button>
        )}
      </td>
    </tr>
  );
}

export function ActiveHoldsList({ holds, onHoldUpdated, onInventoryRefresh }: Props) {
  const [error, setError] = useState<string | null>(null);

  const handleRelease = async (holdId: string) => {
    setError(null);
    try {
      const updated = await releaseHold(holdId);
      onHoldUpdated(updated);
      onInventoryRefresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to release hold');
    }
  };

  return (
    <section className="card">
      <div className="card-header">
        <h2>🔒 Holds</h2>
        <span className="badge badge-active">{holds.filter((h) => h.status === 'active').length} active</span>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {holds.length === 0 ? (
        <p className="muted">No holds yet. Place a hold using the form above.</p>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Hold ID</th>
              <th>Product</th>
              <th className="text-right">Qty</th>
              <th>User</th>
              <th>Status</th>
              <th>Expires In</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {holds.map((hold) => (
              <HoldRow
                key={hold.holdId}
                hold={hold}
                onRelease={handleRelease}
              />
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
