'use strict';

const inventoryRepo = require('../repositories/inventoryRepository');

const SEED_PRODUCTS = [
  { productId: 'prod-001', name: 'Wireless Headphones', quantity: 50 },
  { productId: 'prod-002', name: 'Mechanical Keyboard', quantity: 30 },
  { productId: 'prod-003', name: 'USB-C Hub', quantity: 100 },
  { productId: 'prod-004', name: 'Webcam HD 1080p', quantity: 25 },
  { productId: 'prod-005', name: 'Monitor Stand', quantity: 40 },
  { productId: 'prod-006', name: 'Laptop Cooling Pad', quantity: 60 },
  { productId: 'prod-007', name: 'Ergonomic Mouse', quantity: 75 },
];

/**
 * Seed the database with initial inventory items.
 * Skips products that already exist (idempotent).
 */
async function seedDatabase() {
  let seeded = 0;
  let skipped = 0;

  for (const product of SEED_PRODUCTS) {
    const existing = await inventoryRepo.findByProductId(product.productId);
    if (existing) {
      skipped++;
      continue;
    }

    try {
      await inventoryRepo.createInventoryItem(product);
      seeded++;
    } catch (err) {
      if (err.code === 11000) {
        skipped++; // Race condition: inserted by another process
      } else {
        console.error(`[Seed] Failed to seed product ${product.productId}:`, err.message);
      }
    }
  }

  if (seeded > 0) {
    console.log(`[Seed] Seeded ${seeded} products (${skipped} already existed)`);
  } else {
    console.log(`[Seed] All ${skipped} products already exist — no seeding needed`);
  }
}

module.exports = { seedDatabase };
