// ── API Response types ────────────────────────────────────────────────────────

export interface ApiResponse<T> {
  success: boolean;
  data: T;
  cached?: boolean;
  error?: string;
}

// ── Inventory types ───────────────────────────────────────────────────────────

export interface InventoryItem {
  _id?: string;
  productId: string;
  name: string;
  quantity: number;
  reserved: number;
  available: number;
  createdAt?: string;
  updatedAt?: string;
}

// ── Hold types ────────────────────────────────────────────────────────────────

export type HoldStatus = 'active' | 'released' | 'expired';

export interface Hold {
  _id?: string;
  holdId: string;
  productId: string;
  quantity: number;
  userId: string;
  status: HoldStatus;
  expiresAt: string;
  releasedAt?: string;
  createdAt?: string;
  updatedAt?: string;
}

// ── Request types ─────────────────────────────────────────────────────────────

export interface CreateHoldRequest {
  productId: string;
  quantity: number;
  userId: string;
}
