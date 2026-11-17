# Architecture decisions

Accepted decisions for the lab. ADR numbers remain stable after consolidation. See [system design](../SYSTEM_DESIGN.md) for the current runtime graph.

## ADR 1: Service boundaries

Auth, Schedule and Reservation are implemented. Payment, Analytics and Helpdesk remain planned. Gateway routes external requests; each service enforces permissions independently. Platform supplies shared infrastructure without making services depend on Gateway's executable.

Each capability owns its data. Cross-service reads use HTTP; no cross-schema queries or foreign keys. Policies and SignalR belong to Reservation rather than additional services. These boundaries expose concurrency and failure handling while keeping the lab runnable with a small Docker resource budget. A modular monolith would simplify operations but demonstrate fewer distributed boundaries.

The target payment saga and projections use asynchronous messaging with an outbox/inbox. Currently payment is an in-process simulation, and seat updates use transactional SQL records with polling. Platform's Wolverine helpers are tested but not registered by running services; Compose has no RabbitMQ broker.

## ADR 2: Data ownership and identifiers

Use SQL Server 2025, one database and a schema/login per service. This supports SQL Server concurrency/tuning exercises and keeps local operations small, at the cost of shared resource and failure risk. The amd64 container runs under emulation on Apple Silicon. Service-owned migrations and connection configuration preserve the ownership boundary if databases are separated later; deployment and migration operations would still need adjustment.

Use compact internal relational keys and public GUIDs where another service or API needs an identifier. Natural-key exceptions include the Reservation trip inventory and policy tables. Schedule seed IDs are deterministic; hold groups and bookings explicitly use `Guid.NewGuid()`. There is no universal sequential-GUID helper in the current implementation.

The original identifier experiment reported the following for 100,000 EF Core inserts, 100 rows per save, measuring the unique PublicId index:

| Generator | Insert time | Fragmentation | Pages | Page fill |
| --- | ---: | ---: | ---: | ---: |
| `Guid.NewGuid()` | 4,715 ms | 99.0% | 524 | 70.7% |
| `Guid.CreateVersion7()` | 3,510 ms | 99.0% | 524 | 70.7% |
| EF Core sequential | 3,195 ms | 0.5% | 372 | 99.6% |

These are historical, single-run observations from a removed throwaway fixture, not current application benchmarks. They motivated a sequential-generator proposal; that proposal was not universally implemented. Repeat a representative experiment before changing the deployed identifier strategy. Public IDs do not replace ownership checks.

## ADR 3: Reservation locking

Serialize hold creation, extension, release, checkout and approval by taking an update lock on the trip row within a transaction. This protects seat groups, existing tickets and the user's trip cap together. Interactive writes use a five-second lock timeout. The expiry sweeper uses `UPDLOCK, READPAST, ROWLOCK` to skip busy trips.

The tradeoff is contention across otherwise unrelated seats on the same trip. A single-row optimistic update cannot replace this transaction without an equivalent design for multi-seat atomicity, cap enforcement and retries.

### Contention experiment

On 17 November 2026, the locking test ran on Docker Desktop 29.8.1 (4.80 GB memory), .NET 10.0.1 arm64 and an amd64 SQL Server 2025 container:

```bash
dotnet test tests/Schedule.Tests/Schedule.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~LockingComparisonTests --logger 'console;verbosity=detailed'
```

| Strategy | Elapsed | Attempts/second | Winners | Conflicts |
| --- | ---: | ---: | ---: | ---: |
| Update lock | 4,213 ms | 11.9 | 1 | 49 of 50 |
| Rowversion | 49 ms | 1,023.2 | 1 | 49 of 50 |

Both preserved the one-winner invariant. This was one ordered pair, with update locking first; elapsed time includes connection/barrier setup, and the second arm benefits from warmed resources. The one-row fixture does not model real multi-seat holds, pricing, caps or ticket issuance. It establishes neither production throughput nor a service-level speedup. Retain trip locking for the lab; compare equivalent end-to-end workloads before changing it.
