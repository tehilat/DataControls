# CLAUDE.md — Data Controls System (מערכת בקרות נתונים דינאמית)

## Project goal
A generic web system that lets non-technical business users define data-quality controls
(no SQL, no table names), run them against SQL Server, and see the failing records with the reason.
Stage B adds a free-text → control definition flow via an AI layer (mocked, swappable).
The evaluation focuses on genericity, architecture, security, UX and AI-readiness — not just screens.

## Business domain
Drone delivery fleet (hubs, drones, pilots, customers, deliveries). Full DB spec: `docs/db-instructions.md`.

## Tech stack (mandatory, from the assignment)
- Backend: .NET 10 ASP.NET Core Web API (C#), controllers
- AI service: Python (FastAPI) — separate service, mock provider only (no real AI calls)
- Frontend: Angular (standalone components), Hebrew UI, RTL
- Database: SQL Server

## Repository layout
```
/database            SQL scripts 01_schema … 06_demo, 99_verify.sql (see docs/db-instructions.md)
/backend             DataControls.sln
  DataControls.Domain          entities: Control, ConditionGroup, Condition, Operator enums, metadata models
  DataControls.Application     services, interfaces (IControlRepository, IQueryBuilder, IControlRunner, IAiControlGenerator)
  DataControls.Infrastructure  SQL Server access (Dapper), SqlQueryBuilder, AI service HTTP client
  DataControls.Api             controllers, DTOs, validation, global error handling
  DataControls.Tests           xUnit — mainly QueryBuilder and validation tests
/ai-service          Python FastAPI: /generate-control, providers/ (MockProvider + IProvider interface), schemas.py
/frontend            Angular app
/docs                architecture.md, assumptions-and-limits.md, ai-tools-used.md
```

## Core design rules
1. **Metadata layer is the key to genericity.** Business catalog tables (`meta.Entities`,
   `meta.Fields`, `meta.FieldOptions`, `meta.Relations`, `meta.Operators`, `meta.DataTypeOperators`)
   map business terms in Hebrew ("משלוח", "רחפן", "משקל חבילה") to physical tables/columns and joins.
   No SQL is stored in meta; translation to SQL exists only in code.
   Schemas: `ops` = business data, `meta` = catalog, `ctl` = controls and runs. The UI and the AI only ever see business terms.
   No table or column name may be hard-coded in C#, Python or Angular.
2. **A control is stored as data (JSON condition tree)**, not as SQL:
   root entity + nested groups (AND/OR) of conditions (field, operator, value or other field).
   Saved in `ctl.Controls`. JSON schema is defined in docs/db-instructions.md (version, rootEntity, condition tree,
   operands: value / field / today / now). Reusable and editable.
3. **Separation:** Definition (JSON) → QueryBuilder (translates to SQL) → Runner (executes, read-only).
   Each layer behind an interface.
4. **SQL safety (non-negotiable):**
   - Identifiers (tables/columns/joins) come ONLY from the metadata whitelist; never from user text.
   - All values are SQL parameters — never string-concatenated.
   - Only SELECT is generated; each run executes inside `EXECUTE AS USER = 'controls_runner'` (SELECT on `ops` only), then `REVERT`.
   - Command timeout + row limit (TOP / paging).
   - Validate operator vs field type (e.g. no ">" on text).
5. **Results** return the failing records + a human-readable Hebrew reason per record
   (built from the condition definition), with count, filter and search.
6. **AI layer is replaceable:** .NET calls the Python service via `IAiControlGenerator`.
   Python builds a prompt context from metadata (business terms only), returns a JSON control
   definition in the SAME schema as stage A. Mock provider returns canned answers.
   The generated control is shown to the user for approval before saving/running.

## Conventions
- C#: async all the way, nullable enabled, DTOs separate from domain, ProblemDetails for errors.
- Angular: standalone components, reusable components (condition-builder, field-picker,
  results-table), services for API calls, signals for state, `dir="rtl"` and `lang="he"`.
- All user-facing text in Hebrew. Code, identifiers and comments in English.
- Write tests for QueryBuilder (nested groups, joins, every operator, injection attempts).

## Local environment
- Windows, PowerShell. .NET SDK 10.0.404, Angular CLI 22, Node 24, Python 3.13.
- SQL Server Express instance: `localhost\SQLEXPRESS` (Windows authentication).
  Connection string: `Server=localhost\SQLEXPRESS;Database=DroneOpsControls;Trusted_Connection=True;TrustServerCertificate=True;`
- Network goes through a TLS-inspecting filter (Rimon/Netspark). npm and pip are already configured
  with `C:\certs\rimon-ca.crt`. If a tool fails with a certificate error, configure it to use that CA file;
  never disable TLS verification.

## How to run (update as the project evolves)
- DB: run scripts in /database in order against `localhost\SQLEXPRESS` (always `-C` for the self-signed cert):
  `Get-ChildItem .\database\0[1-5]*.sql | Sort-Object Name | ForEach-Object { sqlcmd -S localhost\SQLEXPRESS -E -C -b -f 65001 -i $_.FullName }`
  then `sqlcmd -S localhost\SQLEXPRESS -E -C -f 65001 -i .\database\99_verify.sql` (expect 14/14 Passed).
  `06_demo_add_weather_alerts.sql` is optional (genericity demo) and re-runnable.
  Seed dates are relative: valid future dates ≥ +365 days, invalid past dates ≥ 30 days back.
- API: `cd backend && dotnet run --project DataControls.Api` (config: `ConnectionStrings:DroneOps`,
  `ControlExecution:{MaxRows,CommandTimeoutSeconds,RunnerUser}`, `Metadata:{AllowedSchemas,CacheSeconds}`)
- AI service: `cd ai-service && .venv\Scripts\activate && uvicorn main:app --reload --port 8001`
- Frontend: `cd frontend && npm start`
- Tests: `cd backend && dotnet test` (unit + integration; integration needs the DB from scripts 01–05 and
  skips itself if it is unreachable). Unit only: `dotnet test --filter "Category!=Integration"`.
  Integration expectations are parsed from `database/99_verify.sql` — keep that file the single source of truth.

## Backend engine (implemented)
See `docs/architecture.md`. Pipeline: `ControlDefinitionSerializer` → `DefinitionBinder` (validation vs metadata,
Hebrew errors, value ranges from `sys.columns`) → `SqlQueryBuilder` → `SqlControlRunner` → `ViolationReasonBuilder`,
orchestrated by `ControlExecutionService`. Text params are always nvarchar; text `isEmpty` uses TRIM.
SQL text for `meta`/`ctl` (the system's own tables) lives only in Infrastructure repositories/providers;
business table/column names never appear in code.

## Working agreement with Claude
- Plan before coding; work in small steps, one layer at a time.
- Never introduce hard-coded table/field names into generic code.
- After each step: build, run tests, and update this file + /docs if the design changed.
