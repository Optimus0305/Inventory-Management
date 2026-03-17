'use strict';

const InventoryItem = require('../models/InventoryItem');

/**
 * Atomically check available stock and deduct the reservation in ONE operation.
 *
 * WHY THIS QUERY IS CONCURRENCY-SAFE
 * ───────────────────────────────────
 * MongoDB evaluates the filter and applies the update as a single document-level
 * atomic operation (no other write can interleave on the same document).
 *
 * The filter condition:
 *   { $expr: { $gte: [ {$subtract:['$quantity','$reserved']}, quantity ] } }
 * means "match only if (quantity - reserved) >= requested quantity".
 *
 * Because the check and the $inc happen atomically:
 *   • Thread A and Thread B both request 5 units with only 5 available.
 *   • One thread wins the internal document lock, its filter matches, reserved += 5.
 *   • The other thread then evaluates the filter: available is now 0, no match → null.
 *
 * This eliminates the classic TOCTOU (Time-of-Check-Time-of-Use) race:
 *   ✗ UNSAFE  read available → check → write reserved  (two separate ops)
 *   ✓ SAFE    check + write in one findOneAndUpdate
 *
 * @param {string} productId
 * @param {number} quantity  – units to reserve
 * @param {import('mongoose').ClientSession|null} session
 * @returns {Promise<import('../models/InventoryItem')|null>}  updated doc or null
 */
async function deductReserved(productId, quantity, session = null) {
  return InventoryItem.findOneAndUpdate(
    {
      productId,
      $expr: {
        $gte: [{ $subtract: ['$quantity', '$reserved'] }, quantity],
      },
    },
    { $inc: { reserved: quantity } },
    { new: true, ...(session && { session }) }
  );
}

/**
 * Restore previously reserved units back to available stock.
 * The filter guards against reserved going negative in an edge-case double-restore.
 *
 * @param {string} productId
 * @param {number} quantity
 * @param {import('mongoose').ClientSession|null} session
 */
async function restoreReserved(productId, quantity, session = null) {
  return InventoryItem.findOneAndUpdate(
    { productId, reserved: { $gte: quantity } },
    { $inc: { reserved: -quantity } },
    { new: true, ...(session && { session }) }
  );
}

/**
 * @param {string} productId
 * @returns {Promise<import('../models/InventoryItem')|null>}
 */
async function findByProductId(productId) {
  return InventoryItem.findOne({ productId });
}

/**
 * @param {object} data
 * @returns {Promise<import('../models/InventoryItem')>}
 */
async function createInventoryItem(data) {
  return InventoryItem.create(data);
}

module.exports = {
  deductReserved,
  restoreReserved,
  findByProductId,
  createInventoryItem,
};
