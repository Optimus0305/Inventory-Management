'use strict';

const mongoose = require('mongoose');

const inventoryItemSchema = new mongoose.Schema(
  {
    productId: {
      type: String,
      required: [true, 'productId is required'],
      unique: true,
      index: true,
      trim: true,
    },
    name: {
      type: String,
      required: [true, 'name is required'],
      trim: true,
    },
    /**
     * Total stock units available in the warehouse.
     */
    quantity: {
      type: Number,
      required: [true, 'quantity is required'],
      min: [0, 'quantity cannot be negative'],
    },
    /**
     * Units currently locked by active holds.
     * Available stock = quantity - reserved.
     */
    reserved: {
      type: Number,
      default: 0,
      min: [0, 'reserved cannot be negative'],
    },
  },
  {
    timestamps: true,
    toJSON: { virtuals: true },
    toObject: { virtuals: true },
  }
);

/**
 * Virtual field: units that can still be reserved.
 */
inventoryItemSchema.virtual('available').get(function () {
  return this.quantity - this.reserved;
});

module.exports = mongoose.model('InventoryItem', inventoryItemSchema);
