'use strict';

const InventoryHold = require('../models/InventoryHold');

/**
 * Persist a new hold document.
 * Must be called inside an active Mongoose ClientSession for transaction safety.
 *
 * @param {object} data
 * @param {import('mongoose').ClientSession|null} session
 */
async function createHold(data, session = null) {
  const [hold] = await InventoryHold.create([data], {
    ...(session && { session }),
  });
  return hold;
}

/**
 * @param {string} holdId
 * @returns {Promise<import('../models/InventoryHold')|null>}
 */
async function findByHoldId(holdId) {
  return InventoryHold.findOne({ holdId });
}

/**
 * Atomically transition a hold from 'active' → 'released'.
 *
 * DOUBLE-RELEASE PREVENTION
 * ─────────────────────────
 * The filter { holdId, status: 'active' } ensures that only the FIRST release
 * request succeeds. A second concurrent or sequential request finds no matching
 * active document and receives null, which the service layer converts to a 409.
 *
 * @param {string} holdId
 * @param {import('mongoose').ClientSession|null} session
 * @returns {Promise<import('../models/InventoryHold')|null>}  updated doc or null
 */
async function markReleased(holdId, session = null) {
  return InventoryHold.findOneAndUpdate(
    { holdId, status: 'active' },
    { $set: { status: 'released', releasedAt: new Date() } },
    { new: true, ...(session && { session }) }
  );
}

/**
 * Atomically transition a hold from 'active' → 'expired'.
 * Only matches holds whose expiresAt has passed, preventing premature expiration.
 *
 * @param {string} holdId
 * @param {import('mongoose').ClientSession|null} session
 * @returns {Promise<import('../models/InventoryHold')|null>}  updated doc or null
 */
async function markExpired(holdId, session = null) {
  return InventoryHold.findOneAndUpdate(
    { holdId, status: 'active', expiresAt: { $lte: new Date() } },
    { $set: { status: 'expired' } },
    { new: true, ...(session && { session }) }
  );
}

/**
 * Find all holds that are active but past their expiration time.
 * Used by the background expiry worker.
 *
 * @returns {Promise<Array<import('../models/InventoryHold')>>}
 */
async function findExpiredActiveHolds() {
  return InventoryHold.find({ status: 'active', expiresAt: { $lte: new Date() } });
}

module.exports = {
  createHold,
  findByHoldId,
  markReleased,
  markExpired,
  findExpiredActiveHolds,
};
