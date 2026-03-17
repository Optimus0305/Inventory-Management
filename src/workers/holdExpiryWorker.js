'use strict';

const { expireAllStaleHolds } = require('../services/holdService');

const DEFAULT_INTERVAL_MS = 60 * 1000; // 1 minute

/**
 * Background worker that periodically scans for active holds whose expiresAt
 * has passed and transitions them to 'expired', restoring reserved inventory.
 *
 * Each hold is processed in its own MongoDB transaction so that a failure on
 * one hold does not prevent others from being expired.
 *
 * NOTE: The worker relies on the same atomic markExpired / restoreReserved
 * logic used by the lazy expiration path in getHold(). Both paths are safe
 * to run concurrently – the findOneAndUpdate filter { status:'active', expiresAt:…}
 * ensures exactly-once processing.
 */
class HoldExpiryWorker {
  /**
   * @param {number} [intervalMs]  How often to run the expiry sweep (ms)
   */
  constructor(intervalMs = DEFAULT_INTERVAL_MS) {
    this.intervalMs = intervalMs;
    this._timer = null;
  }

  start() {
    if (this._timer) return; // already running
    this._timer = setInterval(() => this._run(), this.intervalMs);
    console.log(`HoldExpiryWorker started (interval: ${this.intervalMs}ms)`);
  }

  stop() {
    if (this._timer) {
      clearInterval(this._timer);
      this._timer = null;
      console.log('HoldExpiryWorker stopped');
    }
  }

  async _run() {
    try {
      const { expired, errors } = await expireAllStaleHolds();
      if (expired > 0 || errors > 0) {
        console.log(`HoldExpiryWorker: expired=${expired} errors=${errors}`);
      }
    } catch (err) {
      console.error('HoldExpiryWorker error:', err.message);
    }
  }
}

module.exports = HoldExpiryWorker;
