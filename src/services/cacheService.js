import Redis from 'ioredis';

/**
 * Shared Redis client for caching.
 *
 * Using default options so the client will establish a connection
 * automatically when first used. We do NOT set `lazyConnect: true`
 * together with `enableOfflineQueue: false`, which would require
 * an explicit `connect()` call and could cause silent cache failures
 * if forgotten.
 */
const redisClient = new Redis();

export default redisClient;
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
    _client = new Redis(REDIS_URL, {
      lazyConnect: true,
      // Silently fail if Redis is unavailable — the API should still work without cache
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
    await _client.quit();
    _client = null;
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
