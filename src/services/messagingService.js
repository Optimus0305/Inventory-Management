'use strict';

const amqplib = require('amqplib');
const { randomUUID } = require('crypto');

const RABBITMQ_URL = process.env.RABBITMQ_URL || 'amqp://guest:guest@localhost:5672';

const EXCHANGE = 'inventory.hold.events';
const EXCHANGE_TYPE = 'topic';

// Routing keys matching the .NET topology
const ROUTING_KEYS = {
  HoldCreated: 'hold.created',
  HoldReleased: 'hold.released',
  HoldExpired: 'hold.expired',
};

let _connection = null;
let _channel = null;
let _connected = false;

// ── Connection management ────────────────────────────────────────────────────

async function connect() {
  try {
    _connection = await amqplib.connect(RABBITMQ_URL);
    _channel = await _connection.createConfirmChannel();

    // Declare the topic exchange (idempotent)
    await _channel.assertExchange(EXCHANGE, EXCHANGE_TYPE, {
      durable: true,
      autoDelete: false,
    });

    _connected = true;
    console.log('[RabbitMQ] Connected to broker');

    _connection.on('close', () => {
      _connected = false;
      console.warn('[RabbitMQ] Connection closed');
    });

    _connection.on('error', (err) => {
      _connected = false;
      console.error('[RabbitMQ] Connection error:', err.message);
    });
  } catch (err) {
    // Swallow — the API works without messaging
    console.warn('[RabbitMQ] Could not connect (messaging disabled):', err.message);
    _connected = false;
  }
}

// ── Publish helper ───────────────────────────────────────────────────────────

/**
 * Publish an event to the RabbitMQ exchange.
 * Silently skips if not connected.
 *
 * @param {string} eventType  One of 'HoldCreated', 'HoldReleased', 'HoldExpired'
 * @param {object} payload
 */
async function publishEvent(eventType, payload) {
  if (!_connected || !_channel) return;

  const routingKey = ROUTING_KEYS[eventType];
  if (!routingKey) {
    console.warn(`[RabbitMQ] Unknown event type: ${eventType}`);
    return;
  }

  const message = {
    eventId: randomUUID(),
    eventType,
    occurredAt: new Date().toISOString(),
    ...payload,
  };

  const body = Buffer.from(JSON.stringify(message));

  try {
    await new Promise((resolve, reject) => {
      _channel.publish(
        EXCHANGE,
        routingKey,
        body,
        {
          persistent: true,
          contentType: 'application/json',
          type: eventType,
          messageId: message.eventId,
          timestamp: Math.floor(Date.now() / 1000),
        },
        (err) => (err ? reject(err) : resolve())
      );
    });
  } catch (err) {
    // Messaging failures are non-fatal — the API must keep working
    console.error(`[RabbitMQ] Failed to publish ${eventType}:`, err.message);
  }
}

async function disconnect() {
  try {
    if (_channel) await _channel.close();
    if (_connection) await _connection.close();
  } catch {
    // Ignore
  } finally {
    _channel = null;
    _connection = null;
    _connected = false;
  }
}

module.exports = {
  connect,
  publishEvent,
  disconnect,
  ROUTING_KEYS,
};
