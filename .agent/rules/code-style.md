---
trigger: always_on
---

# Copilot / Agent Project Instructions

These instructions guide automated code assistance in this repository.

## Primary Context Source
- Use `ServerGuide.md` for the authoritative description of architecture, endpoints, models, DTOs, services, and rules.
- After making ANY change to endpoints, models, DTOs, services, database schema, or business rules: UPDATE `ServerGuide.md` in the relevant section.
- Try to use industry/production standard design.
- Explain if there is a better way to approach code rather than fixing what is given. (Libraries, etc.)

## Coding Standards
- Language: C# (.NET 9). Follow idiomatic modern C#.
- Indentation: 4 spaces, no tabs.
- Braces: All blocks use braces; opening brace on same line.
- Naming:
  - Classes, Records, Enums: PascalCase.
  - Methods: PascalCase.
  - Properties: PascalCase.
  - Private fields: `_camelCase` if needed (avoid excess fields when constructor injection works).
  - Parameters & locals: camelCase.
  - Avoid acronyms > 2 letters; if unavoidable, capitalize first letter only (e.g. `DbContext`, not `DBContext`).
- DTOs: Prefer `record` for immutable contracts. For PATCH operations use mutable class with nullable properties.
- Nullability: Enable and respect nullable reference types; avoid `!` suppression except where externally guaranteed.
- Validation: Enforce business rules in controllers/services before persisting. Keep entities lean (no domain logic that depends on infrastructure).
- Asynchrony: Use `async/await` and `Task` everywhere for EF/database calls; no `.Result` or `.Wait()`.
- Error Responses: Use consistent HTTP status codes (400 validation, 401/403 auth, 404 not found, 409 conflict).
- Collections: Prefer `List<T>` for mutable lists; return arrays (`T[]`) or immutable collections when exposed publicly.
- Add comments to explain some code

## Controller Conventions
- Route prefix: `api/<resource>`; avoid mixing pluralization styles.
- HTTP Verbs: GET (read), POST (create/action), PATCH (partial update), DELETE (remove). Avoid PUT unless full replacement semantics are clear.
- Return Types: `ActionResult<T>` for typed responses; `IActionResult` for non-typed actions.
- Authorization: Apply `[Authorize]` at controller level when most actions require auth; override with `[AllowAnonymous]` when needed.
- Validation: Trim strings; check required fields; enforce uniqueness constraints early.

## EF Core & Data
- Context separation: `AuthDbContext` for Identity + friendships; `AppDbContext` for domain entities.
- After adding/changing entities or relationships: create a migration and update both the database and `ServerGuide.md` (Sections 5, 6, 11).
- Indexes: Preserve existing performance indexes; add new ones only when justified (update guide if added).
- Seeding: Use `HasData` cautiously; version seed changes via migrations.

## DTO & Contract Changes
- Treat any modification as a breaking change unless proven otherwise.
- Update `ServerGuide.md` Sections 7 (DTOs) and 9 (Endpoints) immediately.
- Do not silently remove or rename fields; add new fields in a backward-compatible manner when possible.

## Services
- Keep services stateless and registered as `Scoped` unless performance requires otherwise.
- Business logic that is reused across controllers belongs in a service, not in controllers.
- When adding a new service, document it in `ServerGuide.md` Section 8.

## Authentication & Security
- JWT options remain in configuration; do not hardcode secrets.
- Role-based checks: If roles are added, reflect them in token claims and document usage.
- Avoid exposing internal IDs unnecessarily; ensure returned DTOs match documented contracts.

## Testing & Validation (If tests added later)
- Unit tests for services (pure logic like `EventsService.ComputeStatus`).
- Integration tests for controller-route correctness and response shape.

## Migration Workflow (Manual)
```bash
# Auth schema change
dotnet ef migrations add <Name> --context Conquest.Data.Auth.AuthDbContext
dotnet ef database update --context Conquest.Data.Auth.AuthDbContext

# App schema change
dotnet ef migrations add <Name> --context Conquest.Data.App.AppDbContext
dotnet ef database update --context Conquest.Data.App.AppDbContext
```
Update `ServerGuide.md` after successful migration application.

## Performance & Future Extensions
- Batch user lookups for events (consider ToDictionary patterns or joins) if scaling.
- For geospatial complexity, evaluate moving from bounding-box + Haversine to spatial indexes.
- Add pagination to list endpoints before dataset growth causes latency.

## Required Agent Behavior
- ALWAYS consult `ServerGuide.md` before suggesting or implementing changes.
- ALWAYS update `ServerGuide.md` after changes (endpoints, models, DTOs, services, schema, rules).
- NEVER introduce breaking changes without explicit instruction or versioning plan.
- After adding new changes, give a prompt to tell the front end how to use these changes with context(dtos,endpoints,etc).

## Minimal Desired Behavior Summary
Update `ServerGuide.md` whenever you add or modify server code affecting endpoints, models, DTOs, services, or database schema.

---
If you extend these instructions, append new sections rather than altering existing semantics unless performing a deliberate versioned revision.
