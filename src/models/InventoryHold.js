'use strict';

const mongoose = require('mongoose');

const inventoryHoldSchema = new mongoose.Schema(
  {
    holdId: {
      type: String,
      required: [true, 'holdId is required'],
      unique: true,
      index: true,
    },
    productId: {
      type: String,
      required: [true, 'productId is required'],
      index: true,
    },
    quantity: {
      type: Number,
      required: [true, 'quantity is required'],
      min: [1, 'quantity must be at least 1'],
    },
    userId: {
      type: String,
      required: [true, 'userId is required'],
      index: true,
    },
    /**
     * active   – hold is live and inventory is reserved
     * released – user explicitly released the hold; inventory restored
     * expired  – hold was not released before expiresAt; inventory restored
     */
    status: {
      type: String,
      enum: ['active', 'released', 'expired'],
      default: 'active',
      index: true,
    },
    expiresAt: {
      type: Date,
      required: [true, 'expiresAt is required'],
      index: true,
    },
    releasedAt: {
      type: Date,
      default: null,
    },
  },
  { timestamps: true }
);

module.exports = mongoose.model('InventoryHold', inventoryHoldSchema);
