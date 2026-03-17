'use strict';

/**
 * Unit tests for holdService.
 *
 * All I/O (Mongoose sessions, repository calls) is mocked so these run
 * without a real database. The tests validate:
 *   – Correct repository calls with correct parameters
 *   – Correct error types and HTTP status codes
 *   – Correct concurrency-handling logic (what happens when atomic ops return null)
 *   – Edge cases: double release, expired hold access, lazy expiration
 */

jest.mock('../src/repositories/inventoryRepository');
jest.mock('../src/repositories/holdRepository');

// Mock mongoose.startSession to return a lightweight fake session whose
// withTransaction() just calls the callback — good enough for unit tests.
const mockSession = {
  withTransaction: jest.fn().mockImplementation(async (fn) => fn(mockSession)),
  endSession: jest.fn(),
};
jest.mock('mongoose', () => ({
  ...jest.requireActual('mongoose'),
  startSession: jest.fn().mockResolvedValue(mockSession),
}));

const inventoryRepo = require('../src/repositories/inventoryRepository');
const holdRepo = require('../src/repositories/holdRepository');
const {
  createHold,
  releaseHold,
  getHold,
  expireAllStaleHolds,
  HoldError,
} = require('../src/services/holdService');

// Helper: reset all mock calls between tests
beforeEach(() => {
  jest.clearAllMocks();
  mockSession.withTransaction.mockImplementation(async (fn) => fn(mockSession));
});

// ─────────────────────────────────────────────────────────────────
// createHold
// ─────────────────────────────────────────────────────────────────

describe('createHold', () => {
  const validParams = { productId: 'prod-1', quantity: 3, userId: 'user-1' };

  test('calls deductReserved and createHold, returns the new hold', async () => {
    const fakeInventory = { productId: 'prod-1', quantity: 10, reserved: 3 };
    const fakeHold = { holdId: 'hold-abc', productId: 'prod-1', quantity: 3, status: 'active' };

    inventoryRepo.deductReserved.mockResolvedValue(fakeInventory);
    holdRepo.createHold.mockResolvedValue(fakeHold);

    const result = await createHold(validParams);

    expect(inventoryRepo.deductReserved).toHaveBeenCalledWith('prod-1', 3, mockSession);
    expect(holdRepo.createHold).toHaveBeenCalledWith(
      expect.objectContaining({
        productId: 'prod-1',
        quantity: 3,
        userId: 'user-1',
        holdId: expect.any(String),
        expiresAt: expect.any(Date),
      }),
      mockSession
    );
    expect(result).toEqual(fakeHold);
  });

  test('throws HoldError(404) when product does not exist', async () => {
    inventoryRepo.deductReserved.mockResolvedValue(null);
    inventoryRepo.findByProductId.mockResolvedValue(null);

    await expect(createHold(validParams)).rejects.toMatchObject({
      name: 'HoldError',
      statusCode: 404,
    });
    expect(holdRepo.createHold).not.toHaveBeenCalled();
  });

  test('throws HoldError(409) when stock is insufficient', async () => {
    inventoryRepo.deductReserved.mockResolvedValue(null);
    inventoryRepo.findByProductId.mockResolvedValue({
      productId: 'prod-1',
      quantity: 5,
      reserved: 4,
      available: 1,
    });

    await expect(createHold(validParams)).rejects.toMatchObject({
      name: 'HoldError',
      statusCode: 409,
    });
    expect(holdRepo.createHold).not.toHaveBeenCalled();
  });

  test.each([
    [{ productId: '', quantity: 1, userId: 'u' }, 'blank productId'],
    [{ productId: 'p', quantity: 0, userId: 'u' }, 'zero quantity'],
    [{ productId: 'p', quantity: -1, userId: 'u' }, 'negative quantity'],
    [{ productId: 'p', quantity: 1.5, userId: 'u' }, 'float quantity'],
    [{ productId: 'p', quantity: 1, userId: '' }, 'blank userId'],
  ])('throws HoldError(400) for invalid input: %s', async (params) => {
    await expect(createHold(params)).rejects.toMatchObject({
      name: 'HoldError',
      statusCode: 400,
    });
    expect(inventoryRepo.deductReserved).not.toHaveBeenCalled();
  });

  /**
   * Concurrency scenario: Two concurrent holds for the same product where
   * stock only exists for one. The second call's deductReserved returns null
   * (the atomic filter found no matching document).
   */
  test('concurrent requests: second request fails with 409 when stock is exhausted', async () => {
    const fakeInventory = { productId: 'prod-1', quantity: 3, reserved: 3 };
    const fakeHold = { holdId: 'h1', status: 'active' };

    let callCount = 0;
    inventoryRepo.deductReserved.mockImplementation(() => {
      callCount++;
      // First caller gets the stock; second finds nothing available
      return callCount === 1 ? Promise.resolve(fakeInventory) : Promise.resolve(null);
    });
    inventoryRepo.findByProductId.mockResolvedValue({
      productId: 'prod-1', quantity: 3, reserved: 3,
    });
    holdRepo.createHold.mockResolvedValue(fakeHold);

    const results = await Promise.allSettled([
      createHold({ productId: 'prod-1', quantity: 3, userId: 'u1' }),
      createHold({ productId: 'prod-1', quantity: 3, userId: 'u2' }),
    ]);

    const fulfilled = results.filter((r) => r.status === 'fulfilled');
    const rejected = results.filter((r) => r.status === 'rejected');

    expect(fulfilled).toHaveLength(1);
    expect(rejected).toHaveLength(1);
    expect(rejected[0].reason).toMatchObject({ statusCode: 409 });
  });
});

// ─────────────────────────────────────────────────────────────────
// releaseHold
// ─────────────────────────────────────────────────────────────────

describe('releaseHold', () => {
  const HOLD_ID = 'hold-xyz';

  test('marks hold released and restores inventory', async () => {
    const releasedHold = {
      holdId: HOLD_ID,
      productId: 'prod-1',
      quantity: 4,
      status: 'released',
    };
    holdRepo.markReleased.mockResolvedValue(releasedHold);
    inventoryRepo.restoreReserved.mockResolvedValue({});

    const result = await releaseHold(HOLD_ID);

    expect(holdRepo.markReleased).toHaveBeenCalledWith(HOLD_ID, mockSession);
    expect(inventoryRepo.restoreReserved).toHaveBeenCalledWith('prod-1', 4, mockSession);
    expect(result).toEqual(releasedHold);
  });

  test('throws HoldError(404) when hold does not exist', async () => {
    holdRepo.markReleased.mockResolvedValue(null);
    holdRepo.findByHoldId.mockResolvedValue(null);

    await expect(releaseHold(HOLD_ID)).rejects.toMatchObject({
      name: 'HoldError',
      statusCode: 404,
    });
    expect(inventoryRepo.restoreReserved).not.toHaveBeenCalled();
  });

  test('throws HoldError(409) on double release (already released)', async () => {
    holdRepo.markReleased.mockResolvedValue(null);
    holdRepo.findByHoldId.mockResolvedValue({ holdId: HOLD_ID, status: 'released' });

    await expect(releaseHold(HOLD_ID)).rejects.toMatchObject({
      name: 'HoldError',
      statusCode: 409,
    });
    expect(inventoryRepo.restoreReserved).not.toHaveBeenCalled();
  });

  test('throws HoldError(409) when hold is expired', async () => {
    holdRepo.markReleased.mockResolvedValue(null);
    holdRepo.findByHoldId.mockResolvedValue({ holdId: HOLD_ID, status: 'expired' });

    await expect(releaseHold(HOLD_ID)).rejects.toMatchObject({
      name: 'HoldError',
      statusCode: 409,
    });
  });

  /**
   * Concurrent release race: Both requests see the hold as active, but only
   * ONE markReleased can win (filter: status='active'). The loser gets null.
   */
  test('concurrent double release: exactly one succeeds, one gets 409', async () => {
    const releasedHold = { holdId: HOLD_ID, productId: 'prod-1', quantity: 2, status: 'released' };
    let markReleasedCallCount = 0;

    holdRepo.markReleased.mockImplementation(() => {
      markReleasedCallCount++;
      return markReleasedCallCount === 1
        ? Promise.resolve(releasedHold)
        : Promise.resolve(null);
    });
    holdRepo.findByHoldId.mockResolvedValue({ holdId: HOLD_ID, status: 'released' });
    inventoryRepo.restoreReserved.mockResolvedValue({});

    const results = await Promise.allSettled([
      releaseHold(HOLD_ID),
      releaseHold(HOLD_ID),
    ]);

    const fulfilled = results.filter((r) => r.status === 'fulfilled');
    const rejected = results.filter((r) => r.status === 'rejected');

    expect(fulfilled).toHaveLength(1);
    expect(rejected).toHaveLength(1);
    expect(rejected[0].reason).toMatchObject({ statusCode: 409 });

    // Inventory MUST be restored exactly once
    expect(inventoryRepo.restoreReserved).toHaveBeenCalledTimes(1);
  });

  test('throws HoldError(400) for invalid holdId', async () => {
    await expect(releaseHold('')).rejects.toMatchObject({ statusCode: 400 });
    await expect(releaseHold(null)).rejects.toMatchObject({ statusCode: 400 });
  });
});

// ─────────────────────────────────────────────────────────────────
// getHold
// ─────────────────────────────────────────────────────────────────

describe('getHold', () => {
  const HOLD_ID = 'hold-abc';
  const activeHold = {
    holdId: HOLD_ID,
    productId: 'prod-1',
    quantity: 5,
    status: 'active',
    expiresAt: new Date(Date.now() + 60_000), // 1 min in the future
  };

  test('returns the hold when it is active and not expired', async () => {
    holdRepo.findByHoldId.mockResolvedValue(activeHold);

    const result = await getHold(HOLD_ID);

    expect(result).toEqual(activeHold);
    // No expiry processing
    expect(holdRepo.markExpired).not.toHaveBeenCalled();
  });

  test('throws HoldError(404) when hold is not found', async () => {
    holdRepo.findByHoldId.mockResolvedValue(null);

    await expect(getHold(HOLD_ID)).rejects.toMatchObject({
      name: 'HoldError',
      statusCode: 404,
    });
  });

  test('lazily expires an overdue active hold and restores inventory', async () => {
    const expiredActiveHold = {
      holdId: HOLD_ID,
      productId: 'prod-1',
      quantity: 5,
      status: 'active',
      expiresAt: new Date(Date.now() - 1000), // 1 sec in the past
    };
    const nowExpiredHold = { ...expiredActiveHold, status: 'expired' };

    holdRepo.findByHoldId.mockResolvedValue(expiredActiveHold);
    holdRepo.markExpired.mockResolvedValue(nowExpiredHold);
    inventoryRepo.restoreReserved.mockResolvedValue({});

    const result = await getHold(HOLD_ID);

    expect(holdRepo.markExpired).toHaveBeenCalledWith(HOLD_ID, mockSession);
    expect(inventoryRepo.restoreReserved).toHaveBeenCalledWith('prod-1', 5, mockSession);
    expect(result.status).toBe('expired');
  });

  /**
   * Concurrent lazy expiration: Two GET requests arrive simultaneously for an
   * expired hold. Only one markExpired call can win (atomic filter). The other
   * gets null and skips the inventory restore — preventing double restore.
   */
  test('concurrent getHold on expired hold: only one restores inventory', async () => {
    const expiredActiveHold = {
      holdId: HOLD_ID,
      productId: 'prod-1',
      quantity: 5,
      status: 'active',
      expiresAt: new Date(Date.now() - 1000),
    };
    const nowExpiredHold = { ...expiredActiveHold, status: 'expired' };

    holdRepo.findByHoldId.mockResolvedValue(expiredActiveHold);
    inventoryRepo.restoreReserved.mockResolvedValue({});

    let markExpiredCallCount = 0;
    holdRepo.markExpired.mockImplementation(() => {
      markExpiredCallCount++;
      // First caller wins; second finds no active hold to expire
      return markExpiredCallCount === 1
        ? Promise.resolve(nowExpiredHold)
        : Promise.resolve(null);
    });

    // The "loser" re-fetches the hold to return current state
    holdRepo.findByHoldId
      .mockResolvedValueOnce(expiredActiveHold) // first getHold call
      .mockResolvedValueOnce(expiredActiveHold) // second getHold call
      .mockResolvedValue(nowExpiredHold);        // re-fetch after lost race

    await Promise.all([getHold(HOLD_ID), getHold(HOLD_ID)]);

    // restoreReserved must be called exactly ONCE
    expect(inventoryRepo.restoreReserved).toHaveBeenCalledTimes(1);
  });

  test('returns a released hold without modifying it', async () => {
    const releasedHold = { ...activeHold, status: 'released', expiresAt: new Date(0) };
    holdRepo.findByHoldId.mockResolvedValue(releasedHold);

    const result = await getHold(HOLD_ID);
    expect(result.status).toBe('released');
    expect(holdRepo.markExpired).not.toHaveBeenCalled();
  });

  test('throws HoldError(400) for invalid holdId', async () => {
    await expect(getHold('')).rejects.toMatchObject({ statusCode: 400 });
  });
});

// ─────────────────────────────────────────────────────────────────
// expireAllStaleHolds
// ─────────────────────────────────────────────────────────────────

describe('expireAllStaleHolds', () => {
  test('expires all stale holds and restores inventory', async () => {
    const staleHolds = [
      { holdId: 'h1', productId: 'p1', quantity: 3 },
      { holdId: 'h2', productId: 'p2', quantity: 2 },
    ];
    holdRepo.findExpiredActiveHolds.mockResolvedValue(staleHolds);
    holdRepo.markExpired
      .mockResolvedValueOnce({ ...staleHolds[0], status: 'expired' })
      .mockResolvedValueOnce({ ...staleHolds[1], status: 'expired' });
    inventoryRepo.restoreReserved.mockResolvedValue({});

    const { expired, errors } = await expireAllStaleHolds();

    expect(expired).toBe(2);
    expect(errors).toBe(0);
    expect(inventoryRepo.restoreReserved).toHaveBeenCalledTimes(2);
  });

  test('skips hold already processed by another worker (markExpired returns null)', async () => {
    holdRepo.findExpiredActiveHolds.mockResolvedValue([
      { holdId: 'h1', productId: 'p1', quantity: 2 },
    ]);
    holdRepo.markExpired.mockResolvedValue(null); // Another worker already processed it

    const { expired, errors } = await expireAllStaleHolds();

    expect(expired).toBe(0);
    expect(errors).toBe(0);
    expect(inventoryRepo.restoreReserved).not.toHaveBeenCalled();
  });

  test('counts errors when an individual expiry fails', async () => {
    holdRepo.findExpiredActiveHolds.mockResolvedValue([
      { holdId: 'h1', productId: 'p1', quantity: 1 },
    ]);
    holdRepo.markExpired.mockRejectedValue(new Error('DB error'));

    const { expired, errors } = await expireAllStaleHolds();

    expect(expired).toBe(0);
    expect(errors).toBe(1);
  });

  test('returns zero counts when there are no stale holds', async () => {
    holdRepo.findExpiredActiveHolds.mockResolvedValue([]);

    const { expired, errors } = await expireAllStaleHolds();

    expect(expired).toBe(0);
    expect(errors).toBe(0);
  });
});
