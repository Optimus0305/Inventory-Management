'use strict';

const inventoryRepo = require('../repositories/inventoryRepository');

/**
 * GET /api/inventory/:productId
 * Return inventory item with available stock.
 *
 * 200 – item returned
 * 404 – product not found
 */
async function getOne(req, res, next) {
  try {
    const item = await inventoryRepo.findByProductId(req.params.productId);
    if (!item) {
      return res.status(404).json({
        success: false,
        error: `Product '${req.params.productId}' not found`,
      });
    }
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
    return res.status(201).json({ success: true, data: item });
  } catch (err) {
    return next(err);
  }
}

module.exports = { getOne, create };
