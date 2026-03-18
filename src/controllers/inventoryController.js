'use strict';

const inventoryRepo = require('../repositories/inventoryRepository');
const cache = require('../services/cacheService');

/**
 * GET /api/inventory
 * Return all inventory items with available stock.
 * Results are cached for fast repeated reads.
 *
 * 200 – items returned
 */
async function list(req, res, next) {
  try {
    // ── Cache read ───────────────────────────────────────────────────────────
    const cached = await cache.getInventoryList();
    if (cached) {
      return res.status(200).json({ success: true, data: cached, cached: true });
    }

    const items = await inventoryRepo.findAll();

    // ── Cache write ──────────────────────────────────────────────────────────
    await cache.setInventoryList(items);

    return res.status(200).json({ success: true, data: items });
  } catch (err) {
    return next(err);
  }
}

/**
 * GET /api/inventory/:productId
 * Return inventory item with available stock.
 *
 * 200 – item returned
 * 404 – product not found
 */
async function getOne(req, res, next) {
  try {
    const { productId } = req.params;

    // ── Cache read ───────────────────────────────────────────────────────────
    const cached = await cache.getInventoryItem(productId);
    if (cached) {
      return res.status(200).json({ success: true, data: cached, cached: true });
    }

    const item = await inventoryRepo.findByProductId(productId);
    if (!item) {
      return res.status(404).json({
        success: false,
        error: `Product '${productId}' not found`,
      });
    }

    // ── Cache write ──────────────────────────────────────────────────────────
    await cache.setInventoryItem(productId, item);

    return res.status(200).json({ success: true, data: item });
  } catch (err) {
    return next(err);
  }
}

/**
 * POST /api/inventory
 * Seed / create an inventory item (for testing & administration).
 *
 * Body: { productId, name, quantity }
 *
 * 201 – created
 * 400 – invalid input or duplicate productId
 */
async function create(req, res, next) {
  try {
    const { productId, name, quantity } = req.body;
    if (!productId || !name || quantity === undefined) {
      return res.status(400).json({
        success: false,
        error: 'productId, name, and quantity are required',
      });
    }
    const item = await inventoryRepo.createInventoryItem({ productId, name, quantity });

    // Invalidate cache after mutation
    await cache.invalidateInventory();

    return res.status(201).json({ success: true, data: item });
  } catch (err) {
    return next(err);
  }
}

module.exports = { list, getOne, create };
