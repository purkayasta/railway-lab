# RailwayLab specifications

These are target contracts and acceptance criteria, including planned work. [Task status](tasks/todo.md) records what passed verification; [system design](SYSTEM_DESIGN.md) describes what actually runs. Payment is currently simulated, and T7 live authentication acceptance remains open.

## Module contracts

| Specification | Scope |
| --- | --- |
| [Platform](specs/SPEC-platform.md) | Shared infrastructure, gateway, deployment and observability |
| [Auth](specs/SPEC-auth.md) | OpenIddict identity, tokens, permissions and planned management |
| [Web](specs/SPEC-web-shell.md) | Angular shell, login, guards and navigation |
| [Schedule](specs/SPEC-schedule.md) | Timetable, layouts, fares and public search |
| [Reservation](specs/SPEC-reservation.md) | Seat inventory, holds, bookings, tickets and notifications |
| [Payment](specs/SPEC-payment.md) | Planned idempotent payments, saga and reconciliation |
| [Analytics](specs/SPEC-analytics.md) | Planned projections and admin workflows |
| [Helpdesk](specs/SPEC-helpdesk.md) | Planned retrieval and owner-scoped booking assistance |
| [SQL tuning](specs/SPEC-sql-tuning.md) | Planned measured query/index improvements |
| [Interview kit](specs/SPEC-interview-kit.md) | Final demonstration, evidence and rehearsal |

## Data conventions

Business services own their SQL schema and login. Cross-service references use public GUIDs; internal relational keys and foreign keys stay within the owning schema. See [architecture decisions](docs/DECISIONS.md#adr-2-data-ownership-and-identifiers) for identifier exceptions and experiment limits.

Keep future requirements here until their task begins. New projects must deliver useful behavior, be added to `RailwayLab.sln`, and pass relevant build/tests. Project-wide implementation rules live in [AGENTS.md](AGENTS.md).
