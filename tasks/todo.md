# Task status

T10 Payment is the next implementation task. T7 live authentication acceptance remains deferred; authentication is incomplete until it passes. Implement only the active task, using the contracts in [SPEC.md](../SPEC.md).

## Completed

- [x] T0. Repository hygiene: active projects only; no empty scaffolding.
- [x] T1. Platform: shared defaults, health, permissions and authorization audits.
- [x] T2. Gateway: YARP routing, JWT validation, rate limits and internal-path blocking.
- [x] T3. Angular: shell, route guards, login flow and permission-aware navigation.
- [x] T4. Custom OpenIddict Auth: discovery/JWKS, authorize/token/logout and refresh/revocation implementation; live T7 checks remain open.
- [x] T5. Compose: SQL initialization, Auth, Schedule, Reservation, Gateway and Web wiring.
- [x] T6. Browser login: S256 PKCE, code verifier, preserved return route/seat selection, and rejection of anonymous protected access verified.
- [x] T8. Schedule: SQL models/migration/seed, stations/trips/layouts, segment fares and resilient availability lookup. Unknown availability stays distinct from zero.
- [x] T9. Reservation: migration-backed inventory, trip-locked holds, one-time extension/release, policies/audits, server quotes, simulated checkout, tickets, owner/admin booking reads, expiry and SQL-backed SignalR invalidations. Payment is simulated and takes no money.

## Remaining

- [ ] T7. Live refresh rotation and sign-out-everywhere acceptance: verify old-token reuse handling, refresh/session revocation and behavior across browsers/devices. Existing access JWTs can remain valid until their five-minute expiry plus clock skew.
- [ ] T10. Separate Payment service: idempotency, provider simulation, asynchronous saga and reconciliation.
- [ ] T11. Analytics and functional admin views.
- [ ] T12. Helpdesk and final interview kit.

## Last verification — 17 November 2026

- Solution build: zero warnings/errors; C# format verification passed.
- .NET: 117 tests passed — 27 Auth, 67 Platform, 23 Schedule/Reservation. SQL coverage includes migrations, 50 competing holds, overlapping groups, ownership, expiry reclamation, concurrent approval/sweeping, UTC responses and independent notification readers.
- Angular: 34 tests and the production build passed; initial bundle 429.47 kB.
- Real Compose browser checks passed search, seat selection, PKCE login/return, quoted hold, extension, eight-minute countdown, simulated confirmation and My bookings. Checkout sent no price. A second anonymous browser observed a hold in 383 ms in one run; no latency guarantee is implied.
- Desktop/mobile, keyboard skip navigation and 320/768/1440px search layout passed. Three synthetic bookings were created.
- SQL startup crashed in an earlier concurrent run; the final full suite passed with the preview stack stopped. The root mixed-mode launcher has since passed the startup check below.
- Locking measurements and limitations are in [ADR 3](../docs/DECISIONS.md#adr-3-reservation-locking).

## Follow-ups

- Auth management APIs, remote permission snapshots and editable Access screens remain planned in [the Auth specification](../specs/SPEC-auth.md); T7 only covers live session/token acceptance.

- Replace best-effort SQL notification windows/unbounded buffering with durable recovery and backpressure before production use.
- Add bounded, stably ordered booking pagination and measure seed batching opportunities.
- Configure trusted proxies and client/subject rate-limit partitioning; Nginx traffic currently shares proxy-IP buckets.
- Preserve internal fare endpoint isolation; network placement currently protects this anonymous service read.
- Existing pre-migration Schedule volumes and legacy pending bookings may need explicit upgrade handling. Do not reset volumes to hide incompatibilities.

The current architecture and limits are in [SYSTEM_DESIGN.md](../SYSTEM_DESIGN.md). New projects require useful behavior, solution/reference updates and passing build/tests; keep specifications and task status aligned.

## Root startup entry points

`run.sh` defaults to SQL Server in Docker with .NET services and Angular on the host. `init-db` uses the Compose initializer, requiring no host sqlcmd. `docker-compose.yml` is at the root for optional full-container startup. Shell syntax, resolved Compose equivalence and isolated startup/failure/cleanup checks passed. A real mixed-mode smoke test started Docker SQL/init, built the solution and launched all local apps: Auth, Schedule, Reservation and Gateway readiness returned 200; Auth discovery retained its issuer; Angular served on 4200 and its API proxy returned 12 Schedule stations. Ctrl-C cleanup was exercised, leaving SQL running.

Mermaid validation: all 39 blocks across 20 Markdown files rendered successfully with Mermaid CLI 12.2.0. Fixed sequence-message semicolons in SYSTEM_DESIGN.md and the quoted observability subgraph title in the Platform specification.
