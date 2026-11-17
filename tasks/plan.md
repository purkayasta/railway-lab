# Execution plan

Use [todo.md](todo.md) for completion and verification, [SPEC.md](../SPEC.md) for contracts, and [SYSTEM_DESIGN.md](../SYSTEM_DESIGN.md) for the implemented architecture.

Completed: **T0–T6, T8 and T9**, supported by the verification recorded in [todo.md](todo.md#last-verification--17-november-2026).

1. Implement T10 Payment on top of Reservation's quoted checkout and approval transition. Add its project only when work begins.
2. Implement T11 Analytics/admin and T12 Helpdesk/interview kit as their tasks become active.
3. Resume deferred T7 live refresh rotation/global logout validation. T6 passed; authentication remains incomplete until T7 passes.

Keep OpenIddict Auth as the source of truth. Preserve PKCE, refresh rotation, logout behavior, issuer/audience validation and the browser-server flow. Do not scaffold future services, create placeholder documentation or mark tasks complete before verification. Production follow-ups remain listed in [todo.md](todo.md#follow-ups).

Development entry points are `run.sh` and `docker-compose.yml` at the root. Local mode starts SQL Server and database initialization in Docker, then runs .NET services and Angular on the host; `init-db` starts/initializes just the containerized database, and `docker` runs the full container stack.
