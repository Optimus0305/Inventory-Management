'use strict';

require('dotenv').config();

const express = require('express');
const holdsRouter = require('./routes/holds');
const inventoryRouter = require('./routes/inventory');
const errorHandler = require('./middleware/errorHandler');

const app = express();

app.use(express.json());

// Health check
app.get('/health', (_req, res) => res.json({ status: 'ok' }));

// Routes
app.use('/api/inventory', inventoryRouter);
app.use('/api/holds', holdsRouter);

// 404
app.use((_req, res) => res.status(404).json({ success: false, error: 'Not found' }));

// Global error handler (must be last)
app.use(errorHandler);

module.exports = app;
