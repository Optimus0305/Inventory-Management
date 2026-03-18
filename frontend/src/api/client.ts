import type { ApiResponse, InventoryItem, Hold, CreateHoldRequest } from '../types';

const BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:3000';

// ── Generic fetch wrapper ─────────────────────────────────────────────────────

async function apiFetch<T>(
  path: string,
  options?: RequestInit
): Promise<ApiResponse<T>> {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });

  const body = await res.json();

  if (!res.ok) {
    const message = body?.error ?? `HTTP ${res.status}`;
    throw new Error(message);
  }

  return body as ApiResponse<T>;
}

// ── Inventory API ─────────────────────────────────────────────────────────────

export async function fetchInventory(): Promise<InventoryItem[]> {
  const response = await apiFetch<InventoryItem[]>('/api/inventory');
  return response.data;
}

export async function fetchInventoryItem(productId: string): Promise<InventoryItem> {
  const response = await apiFetch<InventoryItem>(`/api/inventory/${productId}`);
  return response.data;
}

// ── Holds API ─────────────────────────────────────────────────────────────────

export async function createHold(request: CreateHoldRequest): Promise<Hold> {
  const response = await apiFetch<Hold>('/api/holds', {
    method: 'POST',
    body: JSON.stringify(request),
  });
  return response.data;
}

export async function fetchHold(holdId: string): Promise<Hold> {
  const response = await apiFetch<Hold>(`/api/holds/${holdId}`);
  return response.data;
}

export async function releaseHold(holdId: string): Promise<Hold> {
  const response = await apiFetch<Hold>(`/api/holds/${holdId}`, {
    method: 'DELETE',
  });
  return response.data;
}
