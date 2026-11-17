# Project Instructions

- Work on the active item in `tasks/todo.md`; `SPEC.md`, `specs/`, and `tasks/plan.md` describe the target, not a request to scaffold every future module.
- The project currently uses a custom OAuth2/OpenID Connect server implemented with OpenIddict, not Keycloak. Keep the Auth service as the source of truth for discovery, JWKS, authorize/token/logout, refresh-token rotation, and global logout behavior.
- Authentication is partially implemented: T6 passed, while T7 live refresh/global logout validation remains deferred. Do not report authentication complete until both checks in `tasks/todo.md` pass.
- Create a project, folder, test suite, or documentation file only in the task that first needs it. Deliver a useful implementation or content with it; no placeholder projects, `Hello World` endpoints, `.gitkeep` files, or empty folders.
- When a task adds or removes a project, update `RailwayLab.sln` and any real references, then build and test the current solution.
- Keep specifications and roadmap entries for planned work; remove implementation scaffolding only when it has no current behavior or dependencies.
- Keep `tasks/todo.md` and `tasks/plan.md` aligned with what is actually implemented. Mark work complete only after its verification passes.
- Any OIDC changes must preserve: PKCE on the SPA, refresh-token rotation, single logout / sign-out-everywhere, issuer and audience validation, and browser-server login flow.
- Write readable code: separate logical steps with blank lines, expand dense control flow and long expressions, and keep transaction boundaries and error handling easy to follow. Do not compress unrelated statements onto one line.
- Format C# changes with `dotnet format` and verify the relevant build and tests before marking work complete.
- Name cancellation tokens `cancellationToken` and declare method parameters as `CancellationToken cancellationToken = default`, except where a required interface or override signature prevents it.
