'use strict';

/**
 * HTTP integration tests for hold and inventory endpoints.
 * holdService is mocked so no real database is needed.
 */

jest.mock('../src/services/holdService', () => {
  const { HoldError } = jest.requireActual('../src/services/holdService');
  return {
    createHold: jest.fn(),
    releaseHold: jest.fn(),
    getHold: jest.fn(),
    HoldError,
  };
});

const request = require('supertest');
const app = require('../src/app');
const holdService = require('../src/services/holdService');
const { HoldError } = jest.requireActual('../src/services/holdService');

// Mock the inventory repository for the inventory endpoints
jest.mock('../src/repositories/inventoryRepository');
const inventoryRepo = require('../src/repositories/inventoryRepository');

beforeEach(() => jest.clearAllMocks());

// ─────────────────────────────────────────────────────────────────
// GET /api/inventory/:productId
// ─────────────────────────────────────────────────────────────────

describe('GET /api/inventory/:productId', () => {
  test('200 with inventory data', async () => {
    inventoryRepo.findByProductId.mockResolvedValue({
      productId: 'prod-1',
      name: 'Widget',
      quantity: 10,
      reserved: 3,
      available: 7,
    });

    const res = await request(app).get('/api/inventory/prod-1');
    expect(res.status).toBe(200);
    expect(res.body.success).toBe(true);
    expect(res.body.data.productId).toBe('prod-1');
  });

  test('404 when product not found', async () => {
    inventoryRepo.findByProductId.mockResolvedValue(null);

    const res = await request(app).get('/api/inventory/ghost');
    expect(res.status).toBe(404);
    expect(res.body.success).toBe(false);
  });
});

describe('POST /api/inventory', () => {
  test('201 creates inventory item', async () => {
    inventoryRepo.createInventoryItem.mockResolvedValue({
      productId: 'p1',
      name: 'Gadget',
      quantity: 20,
      reserved: 0,
    });

    const res = await request(app)
      .post('/api/inventory')
      .send({ productId: 'p1', name: 'Gadget', quantity: 20 });
    expect(res.status).toBe(201);
    expect(res.body.data.productId).toBe('p1');
  });

  test('400 when required fields are missing', async () => {
    const res = await request(app).post('/api/inventory').send({ productId: 'p' });
    expect(res.status).toBe(400);
    expect(res.body.success).toBe(false);
  });
});

// ─────────────────────────────────────────────────────────────────
// POST /api/holds
// ─────────────────────────────────────────────────────────────────

describe('POST /api/holds', () => {
  test('201 creates hold', async () => {
    holdService.createHold.mockResolvedValue({
      holdId: 'h1',
      productId: 'prod-1',
      quantity: 2,
      userId: 'u1',
      status: 'active',
      expiresAt: new Date(Date.now() + 900_000),
    });

    const res = await request(app)
      .post('/api/holds')
      .send({ productId: 'prod-1', quantity: 2, userId: 'u1' });

    expect(res.status).toBe(201);
    expect(res.body.success).toBe(true);
    expect(res.body.data.holdId).toBe('h1');
    expect(res.body.data.status).toBe('active');
  });

  test('400 for invalid body (service throws 400)', async () => {
    holdService.createHold.mockRejectedValue(
      new HoldError('quantity must be a positive integer', 400)
    );

    const res = await request(app)
      .post('/api/holds')
      .send({ productId: 'prod-1', quantity: -1, userId: 'u1' });

    expect(res.status).toBe(400);
    expect(res.body.success).toBe(false);
  });

  test('404 when product not found', async () => {
    holdService.createHold.mockRejectedValue(
      new HoldError("Product 'ghost' not found", 404)
    );

    const res = await request(app)
      .post('/api/holds')
      .send({ productId: 'ghost', quantity: 1, userId: 'u1' });

    expect(res.status).toBe(404);
    expect(res.body.success).toBe(false);
  });

  test('409 when stock is insufficient', async () => {
    holdService.createHold.mockRejectedValue(
      new HoldError('Insufficient stock', 409)
    );

    const res = await request(app)
      .post('/api/holds')
      .send({ productId: 'prod-1', quantity: 100, userId: 'u1' });

    expect(res.status).toBe(409);
    expect(res.body.success).toBe(false);
  });

  test('500 on unexpected error', async () => {
    holdService.createHold.mockRejectedValue(new Error('DB connection lost'));

    const res = await request(app)
      .post('/api/holds')
      .send({ productId: 'p', quantity: 1, userId: 'u' });

    expect(res.status).toBe(500);
    expect(res.body.success).toBe(false);
  });
});

// ─────────────────────────────────────────────────────────────────
// GET /api/holds/:holdId
// ─────────────────────────────────────────────────────────────────

describe('GET /api/holds/:holdId', () => {
  test('200 returns active hold', async () => {
    holdService.getHold.mockResolvedValue({
      holdId: 'h1',
      status: 'active',
    });

    const res = await request(app).get('/api/holds/h1');
    expect(res.status).toBe(200);
    expect(res.body.data.holdId).toBe('h1');
  });

  test('200 returns expired hold (lazy expiration)', async () => {
    holdService.getHold.mockResolvedValue({
      holdId: 'h-expired',
      status: 'expired',
    });

    const res = await request(app).get('/api/holds/h-expired');
    expect(res.status).toBe(200);
    expect(res.body.data.status).toBe('expired');
  });

  test('404 for unknown holdId', async () => {
    holdService.getHold.mockRejectedValue(
      new HoldError("Hold 'missing' not found", 404)
    );

    const res = await request(app).get('/api/holds/missing');
    expect(res.status).toBe(404);
    expect(res.body.success).toBe(false);
  });
});

// ─────────────────────────────────────────────────────────────────
// DELETE /api/holds/:holdId
// ─────────────────────────────────────────────────────────────────

describe('DELETE /api/holds/:holdId', () => {
  test('200 releases an active hold', async () => {
    holdService.releaseHold.mockResolvedValue({
      holdId: 'h1',
      status: 'released',
      releasedAt: new Date(),
    });

    const res = await request(app).delete('/api/holds/h1');
    expect(res.status).toBe(200);
    expect(res.body.data.status).toBe('released');
  });

  test('404 for unknown holdId', async () => {
    holdService.releaseHold.mockRejectedValue(
      new HoldError("Hold 'x' not found", 404)
    );

    const res = await request(app).delete('/api/holds/x');
    expect(res.status).toBe(404);
    expect(res.body.success).toBe(false);
  });

  test('409 on double release', async () => {
    holdService.releaseHold.mockRejectedValue(
      new HoldError("Hold 'h1' cannot be released – current status: released", 409)
    );

    const res = await request(app).delete('/api/holds/h1');
    expect(res.status).toBe(409);
    expect(res.body.success).toBe(false);
  });

  test('409 when hold is expired', async () => {
    holdService.releaseHold.mockRejectedValue(
      new HoldError("Hold 'h2' cannot be released – current status: expired", 409)
    );

    const res = await request(app).delete('/api/holds/h2');
    expect(res.status).toBe(409);
    expect(res.body.success).toBe(false);
  });
});

// ─────────────────────────────────────────────────────────────────
// 404 catch-all
// ─────────────────────────────────────────────────────────────────

describe('Unknown route', () => {
  test('404 for unmatched route', async () => {
    const res = await request(app).get('/no-such-path');
    expect(res.status).toBe(404);
  });
});
