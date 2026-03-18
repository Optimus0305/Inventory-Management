'use strict';

const Redis = require('ioredis');

const REDIS_URL = process.env.REDIS_URL || 'redis://localhost:6379';
const DEFAULT_TTL_SECONDS = parseInt(process.env.CACHE_TTL_SECONDS || '60', 10);

// ── Cache key helpers ────────────────────────────────────────────────────────

const KEYS = {
  inventoryList: () => 'inventory:list',
  inventoryItem: (productId) => `inventory:item:${productId}`,
  hold: (holdId) => `hold:${holdId}`,
};

// ── Redis client ─────────────────────────────────────────────────────────────

let _client = null;

function getClient() {
  if (!_client) {
    // Do NOT use lazyConnect here — with enableOfflineQueue:false and lazyConnect:true
    // the client never auto-connects and all cache operations silently fail.
    // Auto-connect is safe because all commands are wrapped in try/catch.
    _client = new Redis(REDIS_URL, {
      enableOfflineQueue: false,
      maxRetriesPerRequest: 1,
    });

    _client.on('error', (err) => {
      // Log but don't crash — cache is optional
      if (err.code !== 'ECONNREFUSED') {
        console.error('[Redis] error:', err.message);
      }
    });
  }
  return _client;
}

// ── Generic helpers ──────────────────────────────────────────────────────────

async function get(key) {
  try {
    const raw = await getClient().get(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null; // degrade gracefully
  }
}

async function set(key, value, ttlSeconds = DEFAULT_TTL_SECONDS) {
  try {
    await getClient().set(key, JSON.stringify(value), 'EX', ttlSeconds);
  } catch {
    // Ignore cache write failures
  }
}

async function del(...keys) {
  try {
    if (keys.length > 0) await getClient().del(...keys);
  } catch {
    // Ignore cache delete failures
  }
}

// ── Domain-specific helpers ──────────────────────────────────────────────────

async function getInventoryList() {
  return get(KEYS.inventoryList());
}

async function setInventoryList(items) {
  return set(KEYS.inventoryList(), items);
}

async function getInventoryItem(productId) {
  return get(KEYS.inventoryItem(productId));
}

async function setInventoryItem(productId, item) {
  return set(KEYS.inventoryItem(productId), item);
}

async function getHold(holdId) {
  return get(KEYS.hold(holdId));
}

async function setHold(holdId, hold) {
  return set(KEYS.hold(holdId), hold);
}

/**
 * Invalidate all inventory-related cache entries after a mutation.
 * @param {string} [productId]
 */
async function invalidateInventory(productId) {
  const keys = [KEYS.inventoryList()];
  if (productId) keys.push(KEYS.inventoryItem(productId));
  return del(...keys);
}

/**
 * Invalidate a hold from cache after a state change.
 * @param {string} holdId
 */
async function invalidateHold(holdId) {
  return del(KEYS.hold(holdId));
}

async function disconnect() {
  if (_client) {
    try {
      await _client.quit();
    } catch {
      // Ignore quit failures (e.g. connection was never established or already broken)
    } finally {
      _client = null;
    }
  }
}

module.exports = {
  getInventoryList,
  setInventoryList,
  getInventoryItem,
  setInventoryItem,
  getHold,
  setHold,
  invalidateInventory,
  invalidateHold,
  disconnect,
};
