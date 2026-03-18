# AI Usage Documentation

This document describes the AI-assisted development process for the Inventory Hold Microservice assignment.

---

## AI Strategy

### Tools Used

- **GitHub Copilot** — inline code completion and function scaffolding within the editor
- **Claude (claude-sonnet)** — architecture review, code generation for boilerplate-heavy sections, test case design

### How Context Was Managed

Prompts were structured to stay within the architectural constraints defined in the assignment:

1. **Architecture context first** — every session started by referencing the DDD layer structure (Contracts / Domain / Infrastructure / WebApi / Tests) so generated code respected the dependency direction (Domain has no infrastructure dependencies, Infrastructure implements Domain interfaces).

2. **Typed constraints** — prompts included the exact type signatures of interfaces (`IHoldRepository`, `IInventoryRepository`, `IEventPublisher`) so generated implementations matched the contracts.

3. **Concurrency requirements** — specific language was used: "generate a MongoDB `findOneAndUpdate` that atomically checks and deducts stock in a single operation, without a separate read step". This framing prevented the AI from suggesting a naïve read-then-write pattern.

4. **Test isolation** — when requesting tests, the prompt specified: "tests must not require a running database, Redis, or RabbitMQ; mock all external dependencies with Jest mocks / Moq".

---

## Human Audit: Accepted vs. Rejected AI Suggestions

### Accepted

| Area | AI Suggestion | Why Accepted |
|------|---------------|--------------|
| `inventoryRepository.js` | Using `$expr: { $gte: [{ $subtract: ['$quantity', '$reserved'] }, quantity] }` inside `findOneAndUpdate` | Correctly atomic — eliminates TOCTOU race |
| `.NET` `Hold.Release()` / `Hold.Expire()` | State machine guard throws `HoldAlreadyReleasedException` / `HoldAlreadyExpiredException` | Correct domain invariant enforcement |
| Transactional Outbox pattern | Write domain events to an `outbox_messages` collection in the same session as the aggregate | Guarantees exactly-once event delivery even if RabbitMQ is unavailable |
| `RabbitMqTopology` constants | Topic exchange with per-event queues and dead-letter queues | Aligns with production-grade messaging practices |
| `HoldExpiryWorker` (Node.js) | Per-hold transaction with atomic `markExpired` | Safe under concurrent workers |

### Rejected / Modified

| Area | AI Suggestion | Why Rejected / What Changed |
|------|---------------|------------------------------|
| MongoDB transactions in Node.js | AI suggested wrapping every operation in a session transaction even for read-only paths | Removed transaction from `getHold` reads — transactions have overhead and a read followed by optional lazy-expire needs only the expire step transactionalized |
| Redis caching in `holdService` directly | AI inserted Redis calls inside service methods at the same level as business logic | Separated caching into `cacheService.js` (a dedicated module) and kept `holdService.js` focused on business rules |
| RabbitMQ `confirm channel` in Node.js | AI generated a `confirm channel` with `waitForConfirms()` | Replaced with a simpler `confirmChannel.publish()` with callback — the Transactional Outbox guarantees retries, making synchronous broker confirmation less critical for correctness |
| `.NET` `RabbitMqEventPublisher.cs` — duplicate class | AI generated the file twice in one response, creating a duplicate namespace/class definition causing build error CS8954 | Manually removed the duplicate — kept the cleaner version using `CreateChannelOptions(publisherConfirmationsEnabled: true)` |
| Frontend state management | AI suggested Redux Toolkit for state management | Rejected as over-engineering for this scope; used React's built-in `useState` / `useCallback` with custom hooks (`useInventory`, `useHolds`) — simpler, no additional dependencies |
| Frontend auto-polling | AI suggested polling `/api/inventory` every 5 seconds | Rejected — replaced with targeted `refresh()` calls after mutations (creates/releases) to stay in sync without unnecessary network traffic |

---

## Verification

### How AI-Generated Tests Were Validated

1. **Test structure review** — every test was read to confirm it tests a real invariant, not an implementation detail. Tests like "markExpired is called with the correct session" verify observable system behavior (atomicity) rather than internal wiring.

2. **Edge case coverage check** — verified the AI covered:
   - Double-release prevention (409 on second release)
   - Concurrent release race (exactly one succeeds)
   - Lazy expiration with concurrent getHold (inventory restored exactly once)
   - Batch expiry (worker skips holds already expired by lazy path)

3. **Mock fidelity** — confirmed that `withTransaction` is mocked to immediately invoke the callback (matching real Mongoose behavior) so transactional logic is exercised without a live database.

4. **Run confirmation** — all 44 Node.js tests and 22 .NET tests were run and pass in CI without any live infrastructure.

### Manual Verification

- `docker-compose up --build` tested locally; all 4 services start and seed data is visible at `GET /api/inventory`
- Hold lifecycle tested end-to-end: create → verify inventory decremented → release → verify inventory restored
- RabbitMQ management UI confirmed events published to correct queues
- Redis cache confirmed with `redis-cli KEYS '*'` after first GET request
