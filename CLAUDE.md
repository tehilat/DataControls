# CLAUDE.md — רדאר - בקרות איכות נתונים (Data Controls System)

The product name shown to users is **"רדאר - בקרות איכות נתונים"** (icon: Material `radar`).

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
- API: `cd backend && dotnet run --project DataControls.Api` → http://localhost:5149, Swagger UI at `/swagger`,
  OpenAPI at `/openapi/v1.json` (Development). Config: `ConnectionStrings:DroneOps`,
  `ControlExecution:{MaxRows,CommandTimeoutSeconds,RunnerUser,PreviewMaxRows,PreviewDefaultRows}`,
  `Metadata:{AllowedSchemas,CacheSeconds}`, `Cors:AllowedOrigins` (default `http://localhost:4200`).
  `DataControls.Api.http` has sample requests.
- AI service (optional; without it only `/api/ai/*` fails, with 503):
  first time: `cd ai-service && python -m venv .venv && .venv\Scripts\activate && pip install -r requirements-dev.txt`;
  run: `cd ai-service && .venv\Scripts\activate && uvicorn main:app --reload --port 8001` (Swagger at `/docs`);
  tests: `cd ai-service && .venv\Scripts\python -m pytest`. Env `AI_PROVIDER` (default `mock`).
  The API reaches it via `AiService:{BaseUrl,TimeoutSeconds}` (default `http://localhost:8001`, 30s).
- Frontend: `cd frontend && npm install && npm start` → http://localhost:4200 (needs the API on :5149).
  All calls use the relative `/api`, forwarded by `frontend/proxy.conf.json` (no CORS dependency).
  Build: `cd frontend && npx ng build` (must finish with no errors/warnings). Unit tests: `npx ng test --watch=false` (Vitest).
- Tests: `cd backend && dotnet test` (unit + API + integration; integration needs the DB from scripts 01–05 and
  skips itself if it is unreachable). Without DB: `dotnet test --filter "Category!=Integration"` (includes the
  API tests that run on the in-memory catalog via `CatalogOnlyApiFactory`).
  Integration expectations are parsed from `database/99_verify.sql` — keep that file the single source of truth.

## Backend engine (implemented)
See `docs/architecture.md`. Pipeline: `ControlDefinitionSerializer` → `DefinitionBinder` (validation vs metadata,
Hebrew errors, value ranges from `sys.columns`) → `SqlQueryBuilder` → `SqlControlRunner` → `ViolationReasonBuilder`,
orchestrated by `ControlExecutionService`. Text params are always nvarchar; text `isEmpty` uses TRIM.
SQL text for `meta`/`ctl` (the system's own tables) lives only in Infrastructure repositories/providers;
business table/column names never appear in code.

## API layer (implemented)
See `docs/architecture.md` ("שכבת ה-API", "הנחות ומגבלות"). Controllers: `metadata` (business-only catalog via
`BusinessCatalogService`), `controls` (CRUD + rowVersion, activate/deactivate, validate, preview ≤100 rows, run),
`runs` (history, run detail, stored violations with paging/search/typed filter+sort). Run results are stored in
`ctl.ControlRunViolations` in canonical JSON (`CanonicalValueJson`); typed sort/filter use a fixed set of
`TRY_CONVERT` expressions (`TypedJsonValue`), keys must be in the run's column snapshot. Errors are Hebrew
ProblemDetails (`ApiExceptionHandler`, `ModelStateProblems`) with JSON paths. `GeneratedSql` is never exposed.

## Frontend (implemented)
See `docs/architecture.md` ("Frontend") and `frontend/README.md`. Angular 22 zoneless, standalone, signals, Angular Material (M3, Heebo),
lazy routes: `/controls`, `/controls/new` (`?copyFrom=id` duplicate, `?draft=1` take a definition from `ControlDraftService`),
`/controls/:id/edit`, `/controls/ai` (stage B), `/runs` (`?controlId=`), `/runs/:runId`.
- `/controls/ai`: free text + the 5 example requests the Mock recognizes → `AiApi.suggestControl` (`POST /api/ai/suggest-control`);
  shows server summary, explanation, confidence, warnings, validation errors; "פתח בעורך" hands the definition to
  `ControlDraftService` → `/controls/new?draft=1` (source AI). Nothing is saved or run there. "מה נשלח ל-AI" loads
  `GET /api/ai/prompt-preview` on demand. 503 / no connection → Hebrew "service unavailable" notice with a link to manual creation.
- `core/api`: typed DTOs + `MetadataApi`/`ControlsApi`/`RunsApi`; `apiErrorInterceptor` turns every failure into `ApiError`
  (Hebrew ProblemDetails, `validationErrors` with JSON paths, `runId`). `ProblemAlert` shows it; `describePath` turns a path into "תנאי 2 › 1".
- `core/definition`: wire model + editor tree (ids), conversion, local completeness checks with server-style paths, Hebrew summary.
- Condition builder: `ConditionContext` (per form: fields, operators, errors) → recursive `condition-group` ↔ `condition-rule`
  → shared `field-picker` (grouped by relation path, searchable), `operator-select`, `value-input` (by type), `operand-editor`
  (value / other field / today ± days / now). Errors from `controls/validate` (debounced) and from save are shown on the rule by path.
  The "וגם"/"או" label between conditions is a button that switches the whole group's logic.
- `ControlForm` is reusable and takes a `seed` (`ControlSeed`) — stage B should hand the AI suggestion to it (source `AI`).
- Results: `results-table` (record id column — the real key, not the row number; the display name only when it differs;
  generic columns from the run, list values by name, server-side sort), typed filters (canonical values), paging.
- Look: toolbar and main buttons in dark blue `#12233d`; the toolbar never scrolls (texts collapse at 1000px / 760px).
- Reasons for `isEmpty` / `isNotEmpty`: "{field}: חסר ערך" / "{field}: קיים ערך" (`ViolationReasonBuilder`).

## AI layer (implemented)
See `docs/architecture.md` ("שכבת ה-AI") and `ai-service/README.md`. `POST /api/ai/suggest-control {text}` →
`AiSuggestionService` builds the business catalog (`IBusinessCatalogService`, flattened field paths, no physical names),
calls `IAiControlGenerator` (`HttpAiControlGenerator` → Python `/generate-control`), then parses + binds the answer with the
same `ControlDefinitionSerializer` / `DefinitionBinder` as manual definitions and adds a Hebrew `summary`
(`DefinitionSummaryBuilder`). It never saves or runs (no repository/runner dependency); the user saves via
`POST /api/controls` with source `AI`. `GET /api/ai/prompt-preview?text=` shows the model prompt. AI down → 503 ProblemDetails.
Python side: `AiProvider` interface, data-driven `MockProvider` (`mock_scenarios.json`), `PromptBuilder`, catalog validation.
Swapping the model = new `AiProvider` in Python + `AI_PROVIDER`; no .NET change.

## Working agreement with Claude
- Plan before coding; work in small steps, one layer at a time.
- Never introduce hard-coded table/field names into generic code.
- After each step: build, run tests, and update this file + /docs if the design changed.
