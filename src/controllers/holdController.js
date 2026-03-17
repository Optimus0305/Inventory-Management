'use strict';

const { createHold, releaseHold, getHold } = require('../services/holdService');

/**
 * POST /api/holds
 * Create an inventory hold.
 *
 * Body: { productId, quantity, userId }
 *
 * 201 – hold created
 * 400 – invalid input
 * 404 – product not found
 * 409 – insufficient stock
 */
async function create(req, res, next) {
  try {
    const { productId, quantity, userId } = req.body;
    const hold = await createHold({ productId, quantity, userId });
    return res.status(201).json({ success: true, data: hold });
  } catch (err) {
    return next(err);
  }
}

/**
 * GET /api/holds/:holdId
 * Fetch a hold. Lazily expires the hold if it has passed its expiration time.
 *
 * 200 – hold returned (status may be 'expired' if just lazily expired)
 * 400 – invalid holdId
 * 404 – hold not found
 */
async function getOne(req, res, next) {
  try {
    const hold = await getHold(req.params.holdId);
    return res.status(200).json({ success: true, data: hold });
  } catch (err) {
    return next(err);
  }
}

/**
 * DELETE /api/holds/:holdId
 * Release an active hold and restore reserved inventory.
 *
 * 200 – hold released
 * 400 – invalid holdId
 * 404 – hold not found
 * 409 – hold already released or expired
 */
async function release(req, res, next) {
  try {
    const hold = await releaseHold(req.params.holdId);
    return res.status(200).json({ success: true, data: hold });
  } catch (err) {
    return next(err);
  }
}

module.exports = { create, getOne, release };
