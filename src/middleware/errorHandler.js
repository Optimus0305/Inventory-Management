'use strict';

const { HoldError } = require('../services/holdService');

/**
 * Global error handler.
 * Converts HoldError (domain errors) to structured JSON responses with the
 * correct HTTP status code. All other errors are treated as 500.
 *
 * @param {Error} err
 * @param {import('express').Request} req
 * @param {import('express').Response} res
 * @param {import('express').NextFunction} _next
 */
// eslint-disable-next-line no-unused-vars
function errorHandler(err, req, res, _next) {
  if (err.name === 'HoldError') {
    return res.status(err.statusCode).json({ success: false, error: err.message });
  }

  // Mongoose duplicate key
  if (err.code === 11000) {
    return res.status(409).json({ success: false, error: 'Duplicate key conflict' });
  }

  // Mongoose validation error
  if (err.name === 'ValidationError') {
    const messages = Object.values(err.errors).map((e) => e.message);
    return res.status(400).json({ success: false, error: messages.join(', ') });
  }

  console.error(err);
  return res.status(500).json({ success: false, error: 'Internal server error' });
}

module.exports = errorHandler;
