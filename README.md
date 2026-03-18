# Inventory Hold Microservice

A production-quality microservice for managing temporary inventory holds during checkout. When a customer begins checkout, their items are reserved so they can't be sold to another customer. Holds expire after a configurable duration.

---

## Architecture Overview

```
┌────────────────────────────────────────────────────┐
│  React/TypeScript Frontend (Vite, port 5173)        │
└────────────────────┬───────────────────────────────┘
                     │ HTTP
┌────────────────────▼───────────────────────────────┐
│  Node.js API (Express, port 3000)                   │
│  ├── Routes: /api/holds, /api/inventory             │
│  ├── Services: HoldService, CacheService, Messaging │
│  └── Workers: HoldExpiryWorker                      │
└────┬────────────────┬────────────────┬─────────────┘
     │                │                │
┌────▼────┐  ┌────────▼────┐  ┌────────▼────┐
│ MongoDB │  │    Redis     │  │  RabbitMQ   │
│ (store) │  │  (cache)    │  │ (messaging) │
└─────────┘  └─────────────┘  └─────────────┘
```

### DDD .NET Reference Implementation

A companion `.NET 10` Domain-Driven Design implementation lives in `src/` alongside the running Node.js service. It demonstrates:

- **InventoryHold.Contracts** — DTOs, requests, responses
- **InventoryHold.Domain** — Aggregates (`Hold`, `InventoryItem`), domain events, exceptions
- **InventoryHold.Infrastructure** — MongoDB, RabbitMQ (Transactional Outbox), and service implementations
- **InventoryHold.WebApi** — ASP.NET Core controllers and DI wiring
- **tests/InventoryHold.Tests** — xUnit tests with Moq mocking

---

## Quick Start

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker + Docker Compose)
- Node.js 20+ (for the frontend only)

### One-Command Startup

```bash
docker-compose up --build
```

This starts:
- **MongoDB 7** with replica set (required for multi-document transactions)
- **Redis 7** for caching
- **RabbitMQ 3** with management UI
- **Node.js API** on port 3000 (with database seeding on startup)

Wait for all services to be healthy (approx. 30–60 seconds on first run), then:

```
API:          http://localhost:3000
RabbitMQ UI:  http://localhost:15672  (guest / guest)
```

### Frontend

```bash
cd frontend
npm install
npm run dev
```

Open http://localhost:5173

---

## API Reference

### Inventory

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/inventory` | List all inventory items |
| `GET` | `/api/inventory/:productId` | Get single inventory item |
| `POST` | `/api/inventory` | Create inventory item (admin) |

### Holds

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/holds` | Create a new hold |
| `GET` | `/api/holds/:holdId` | Get hold by ID |
| `DELETE` | `/api/holds/:holdId` | Release a hold |

#### Example: Create Hold

```bash
curl -X POST http://localhost:3000/api/holds \
  -H "Content-Type: application/json" \
  -d '{"productId":"prod-001","quantity":2,"userId":"user-123"}'
```

#### Example: Release Hold

```bash
curl -X DELETE http://localhost:3000/api/holds/<holdId>
```

#### Example: List Inventory

```bash
curl http://localhost:3000/api/inventory
```

---

## Configuration

All settings are configurable via environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `MONGODB_URI` | `mongodb://localhost:27017/inventory` | MongoDB connection string |
| `REDIS_URL` | `redis://localhost:6379` | Redis connection URL |
| `RABBITMQ_URL` | `amqp://guest:guest@localhost:5672` | RabbitMQ connection URL |
| `PORT` | `3000` | API server port |
| `HOLD_DURATION_MINUTES` | `15` | Default hold duration |
| `CACHE_TTL_SECONDS` | `60` | Redis cache TTL |

---

## Running Tests

### Node.js Tests (Jest)

```bash
npm test               # run all tests
npm run test:coverage  # with coverage report
```

All tests run without any infrastructure (MongoDB, Redis, RabbitMQ are all mocked).

### .NET Tests (xUnit)

```bash
dotnet test InventoryHold.slnx
```

---

## Design Decisions

### Concurrency Safety

Stock deduction uses a **single atomic `findOneAndUpdate`** with a compound filter:
```javascript
{ productId, $expr: { $gte: [{ $subtract: ['$quantity', '$reserved'] }, quantity] } }
```
This prevents the classic TOCTOU (Time-of-Check-Time-of-Use) race condition where two concurrent requests both see sufficient stock and both proceed to reserve it.

### Hold Lifecycle

```
active ──→ released  (explicit DELETE /api/holds/:id)
active ──→ expired   (lazy on GET or background worker sweep)
```

The `HoldExpiryWorker` sweeps every 60 seconds for expired holds. Both the worker and the lazy expiration path use the same atomic `markExpired` operation, preventing double-restore of inventory.

### Caching Strategy

- **GET /api/inventory** — cached for `CACHE_TTL_SECONDS` (default: 60s)
- **GET /api/inventory/:productId** — cached per product
- **GET /api/holds/:holdId** — cached for active holds only
- Cache is invalidated on every mutation (create/release hold, create inventory item)
- Redis failures degrade gracefully — the API continues to work without caching

### RabbitMQ Topology

```
Exchange: inventory.hold.events (topic)
  ├── hold.created   → inventory.hold.created   (+ DLQ)
  ├── hold.released  → inventory.hold.released  (+ DLQ)
  └── hold.expired   → inventory.hold.expired   (+ DLQ)

Dead-letter exchange: inventory.hold.events.dlx
```

Events are published non-blocking — RabbitMQ failures do not affect API availability.

### Seeded Products

On startup, the API seeds 7 products if they don't already exist:
- Wireless Headphones (50 units)
- Mechanical Keyboard (30 units)
- USB-C Hub (100 units)
- Webcam HD 1080p (25 units)
- Monitor Stand (40 units)
- Laptop Cooling Pad (60 units)
- Ergonomic Mouse (75 units)

---

## Project Structure

```
├── server.js                   # Entry point
├── src/
│   ├── app.js                  # Express app
│   ├── config/
│   │   ├── database.js         # MongoDB connection
│   │   └── seed.js             # Database seeding
│   ├── controllers/            # Route handlers
│   ├── middleware/             # Error handler
│   ├── models/                 # Mongoose schemas
│   ├── repositories/           # Data access layer
│   ├── routes/                 # Express routers
│   ├── services/
│   │   ├── holdService.js      # Core business logic
│   │   ├── cacheService.js     # Redis caching
│   │   └── messagingService.js # RabbitMQ publishing
│   └── workers/
│       └── holdExpiryWorker.js # Background expiry sweep
├── tests/
│   ├── holdService.test.js     # Unit tests (business logic)
│   └── holdController.test.js  # HTTP integration tests
├── frontend/                   # React/TypeScript/Vite SPA
├── src/InventoryHold.*/        # .NET 10 DDD implementation
├── tests/InventoryHold.Tests/  # .NET xUnit tests
├── docker-compose.yml
├── Dockerfile
└── AI-USAGE.md
```
