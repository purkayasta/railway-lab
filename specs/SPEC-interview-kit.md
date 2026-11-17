# SPEC-interview-kit

Depends on: completed modules. Status: planned under T12. Existing guides describe the current implementation; final rehearsals and the full-system demo remain outstanding.

## Objective

Demonstrate the project in five minutes and explain its implementation, failures and tradeoffs with concrete evidence.

## Deliverables

- Keep [README](../README.md), [system design](../SYSTEM_DESIGN.md) and [architecture decisions](../docs/DECISIONS.md) accurate and concise.
- Prepare a runnable demo and short recording: login, search, select seats, observe another browser's hold, confirm and review a booking. Add real Payment, reconciliation, admin and Helpdesk steps only after their tasks pass.
- Prepare at least 40 answers in the author's own words, including five per completed module, based on actual code and measured results. Create a separate question/answer artifact only when T12 needs it.
- Practice the architecture, OIDC flow, seat lifecycle and eventual payment saga diagrams. Explain the hold SQL, an execution plan, a trace, Dockerfiles and Compose configuration.
- Use [SQL Server study notes](../docs/sql-server-notes.md) for database internals practice: run at least four disposable-database labs covering indexes, row layout, log/commit behavior and crash recovery; answer the 18 review questions aloud. Validate uncertain reference claims before presenting them.
- Produce a tuning write-up with query/plan/measurement evidence when the SQL-tuning task begins. Capture trace screenshots only after observability infrastructure exists.

## Acceptance criteria

1. Documented startup and the current demo work from a fresh checkout.
2. Mandatory invariant tests pass in CI once CI is implemented.
3. Decisions distinguish implemented behavior from future design; performance claims cite measured experiments and limits.
4. Each completed module has an explainable decision, diagram and five own-word answers.
5. Rehearse the question set twice, with an extra pass for weak areas; redraw the system and explain the critical code without notes.
6. Clearly identify unfinished/cut features and explain AI-assisted work honestly.

## Boundaries

Do not create empty study templates, claim planned services as built or demonstrate unsupported payment/admin flows. Keep the current architecture guide separate from the future target specifications.
