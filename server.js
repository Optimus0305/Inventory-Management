'use strict';

require('dotenv').config();

const app = require('./src/app');
const { connectDB } = require('./src/config/database');
const HoldExpiryWorker = require('./src/workers/holdExpiryWorker');

const PORT = process.env.PORT || 3000;

(async () => {
  await connectDB();
  console.log('Connected to MongoDB');

  const worker = new HoldExpiryWorker();
  worker.start();

  app.listen(PORT, () => {
    console.log(`Inventory Management API listening on port ${PORT}`);
  });

  process.on('SIGTERM', async () => {
    worker.stop();
    process.exit(0);
  });
})();
