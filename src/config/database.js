'use strict';

const mongoose = require('mongoose');

/**
 * Connect to MongoDB. Requires a replica set for transaction support.
 * For local development use docker-compose or mongod --replSet rs0.
 */
async function connectDB(uri) {
  const conn = await mongoose.connect(uri || process.env.MONGODB_URI, {
    serverSelectionTimeoutMS: 5000,
  });
  return conn;
}

async function disconnectDB() {
  await mongoose.disconnect();
}

module.exports = { connectDB, disconnectDB };
