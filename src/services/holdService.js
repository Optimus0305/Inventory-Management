'use strict';

const mongoose = require('mongoose');
const { v4: uuidv4 } = require('uuid');
const inventoryRepo = require('../repositories/inventoryRepository');
const holdRepo = require('../repositories/holdRepository');

const HOLD_DURATION_MS =
  parseInt(process.env.HOLD_DURATION_MINUTES || '15', 10) * 60 * 1000;

// ---------------------------------------------------------------------------
// Domain Error
// ---------------------------------------------------------------------------

class HoldError extends Error {
  /**
   * @param {string} message
   * @param {number} statusCode  HTTP status code the controller should use
   */
  constructor(message, statusCode) {
    super(message);
    this.name = 'HoldError';
    this.statusCode = statusCode;
  }
}

// ---------------------------------------------------------------------------
// Create Hold
// ---------------------------------------------------------------------------

/**
 * Create an inventory hold for a product.
 *
 * CONCURRENCY SAFETY
 * ──────────────────
 * Inventory deduction uses a single findOneAndUpdate with a compound filter
 * that checks available stock AND applies the increment atomically. This is
 * the only correct way to prevent overselling under concurrent load.
 *
 * A MongoDB transaction wraps the inventory deduction and hold insert so that
 * a crash between the two steps cannot leave the system in an inconsistent state
 * (reserved inventory with no matching hold record).
 *
 * @param {{ productId: string, quantity: number, userId: string }} params
 * @returns {Promise<import('../models/InventoryHold')>}
 * @throws {HoldError}
 */
async function createHold({ productId, quantity, userId }) {
  if (!productId || typeof productId !== 'string' || !productId.trim()) {
    throw new HoldError('productId is required', 400);
  }
  if (!Number.isInteger(quantity) || quantity <= 0) {
    throw new HoldError('quantity must be a positive integer', 400);
  }
  if (!userId || typeof userId !== 'string' || !userId.trim()) {
    throw new HoldError('userId is required', 400);
  }

  const session = await mongoose.startSession();

  try {
    let hold;

    await session.withTransaction(async () => {
      // ── Atomic stock check + deduction ──────────────────────────────────
      // findOneAndUpdate evaluates the filter and applies $inc in ONE operation.
      // No concurrent request can interleave between the check and the write.
      const updatedInventory = await inventoryRepo.deductReserved(
        productId,
        quantity,
        session
      );

      if (!updatedInventory) {
        // Could not match: either product missing or stock insufficient.
        // We check which one to give an accurate error (outside the txn scope).
        const item = await inventoryRepo.findByProductId(productId);
        if (!item) {
          throw new HoldError(`Product '${productId}' not found`, 404);
        }
        throw new HoldError(
          `Insufficient stock for '${productId}'. ` +
            `Available: ${item.quantity - item.reserved}, Requested: ${quantity}`,
          409
        );
      }

      // ── Create hold record ───────────────────────────────────────────────
      const expiresAt = new Date(Date.now() + HOLD_DURATION_MS);
      hold = await holdRepo.createHold(
        { holdId: uuidv4(), productId, quantity, userId, expiresAt },
        session
      );
    });

    return hold;
  } finally {
    session.endSession();
  }
}

// ---------------------------------------------------------------------------
// Release Hold
// ---------------------------------------------------------------------------

/**
 * Release an active inventory hold and restore the reserved stock.
 *
 * DOUBLE-RELEASE PREVENTION
 * ─────────────────────────
 * markReleased uses findOneAndUpdate with filter { status: 'active' }.
 * The first release wins the document lock and flips the status; subsequent
 * calls find no matching document and receive null → 409 Conflict.
 *
 * The transaction guarantees that the hold status update and the inventory
 * restore both commit or both roll back.
 *
 * @param {string} holdId
 * @returns {Promise<import('../models/InventoryHold')>}
 * @throws {HoldError}
 */
async function releaseHold(holdId) {
  if (!holdId || typeof holdId !== 'string') {
    throw new HoldError('holdId is required', 400);
  }

  const session = await mongoose.startSession();

  try {
    let releasedHold;

    await session.withTransaction(async () => {
      // ── Atomic status transition: active → released ──────────────────────
      releasedHold = await holdRepo.markReleased(holdId, session);

      if (!releasedHold) {
        // Determine exact failure reason for a useful error message.
        const existing = await holdRepo.findByHoldId(holdId);
        if (!existing) {
          throw new HoldError(`Hold '${holdId}' not found`, 404);
        }
        throw new HoldError(
          `Hold '${holdId}' cannot be released – current status: ${existing.status}`,
          409
        );
      }

      // ── Restore reserved inventory ───────────────────────────────────────
      await inventoryRepo.restoreReserved(
        releasedHold.productId,
        releasedHold.quantity,
        session
      );
    });

    return releasedHold;
  } finally {
    session.endSession();
  }
}

// ---------------------------------------------------------------------------
// Get Hold
// ---------------------------------------------------------------------------

/**
 * Fetch a hold, applying lazy expiration if the hold's time has passed but
 * it has not yet been marked expired.
 *
 * LAZY EXPIRATION SAFETY
 * ──────────────────────
 * markExpired uses findOneAndUpdate with filter
 *   { holdId, status: 'active', expiresAt: { $lte: now } }
 * If two concurrent GET requests race to expire the same hold, only ONE will
 * match and do the inventory restore; the second receives null and skips it.
 *
 * @param {string} holdId
 * @returns {Promise<import('../models/InventoryHold')>}
 * @throws {HoldError}
 */
async function getHold(holdId) {
  if (!holdId || typeof holdId !== 'string') {
    throw new HoldError('holdId is required', 400);
  }

  let hold = await holdRepo.findByHoldId(holdId);

  if (!hold) {
    throw new HoldError(`Hold '${holdId}' not found`, 404);
  }

  // ── Lazy expiration ─────────────────────────────────────────────────────
  if (hold.status === 'active' && hold.expiresAt <= new Date()) {
    const session = await mongoose.startSession();

    try {
      await session.withTransaction(async () => {
        // Atomic: only one concurrent request will succeed at transitioning
        const expiredHold = await holdRepo.markExpired(holdId, session);

        if (expiredHold) {
          // We won the race – restore inventory
          await inventoryRepo.restoreReserved(
            expiredHold.productId,
            expiredHold.quantity,
            session
          );
          hold = expiredHold;
        }
        // If null, another request already expired it; hold will be re-fetched
        // after the transaction block.
      });

      if (hold.status === 'active') {
        // Our transaction found nothing to expire (lost the race).
        hold = await holdRepo.findByHoldId(holdId);
      }
    } finally {
      session.endSession();
    }
  }

  return hold;
}

// ---------------------------------------------------------------------------
// Batch Expire (used by background worker)
// ---------------------------------------------------------------------------

/**
 * Expire all holds whose expiresAt has passed and restore their reserved stock.
 * Intended to be called periodically by the HoldExpiryWorker.
 *
 * Each hold is processed in its own transaction so a failure on one does not
 * block the rest.
 *
 * @returns {Promise<{ expired: number, errors: number }>}
 */
async function expireAllStaleHolds() {
  const stale = await holdRepo.findExpiredActiveHolds();
  let expired = 0;
  let errors = 0;

  for (const hold of stale) {
    const session = await mongoose.startSession();
    try {
      await session.withTransaction(async () => {
        const expiredHold = await holdRepo.markExpired(hold.holdId, session);
        if (expiredHold) {
          await inventoryRepo.restoreReserved(
            expiredHold.productId,
            expiredHold.quantity,
            session
          );
          expired++;
        }
        // else: already processed by a concurrent request – skip silently
      });
    } catch (err) {
      errors++;
    } finally {
      session.endSession();
    }
  }

  return { expired, errors };
}

module.exports = {
  createHold,
  releaseHold,
  getHold,
  expireAllStaleHolds,
  HoldError,
};
