'use strict';

require('dotenv').config();

const app = require('./src/app');
const { connectDB } = require('./src/config/database');
const { seedDatabase } = require('./src/config/seed');
const HoldExpiryWorker = require('./src/workers/holdExpiryWorker');
const messaging = require('./src/services/messagingService');

const PORT = process.env.PORT || 3000;

(async () => {
  await connectDB();
  console.log('Connected to MongoDB');

  // Seed initial inventory data (idempotent)
  await seedDatabase();

  // Connect to RabbitMQ (non-blocking — app works without it)
  await messaging.connect();

  const worker = new HoldExpiryWorker();
  worker.start();

  app.listen(PORT, () => {
    console.log(`Inventory Management API listening on port ${PORT}`);
  });

  const shutdown = async () => {
    worker.stop();
    await messaging.disconnect();
    process.exit(0);
  };

  process.on('SIGTERM', shutdown);
  process.on('SIGINT', shutdown);
})();
