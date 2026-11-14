# 📡 רדאר - בקרות איכות נתונים — Frontend

Angular 22 (standalone components, signals, zoneless) + Angular Material. Hebrew-only UI, `dir="rtl"`, `lang="he"`.

## Run

```powershell
npm install
npm start        # ng serve → http://localhost:4200
```

The API must run on `http://localhost:5149` (`cd ../backend; dotnet run --project DataControls.Api`).
All calls go to the relative path `/api`, which the dev server forwards through `proxy.conf.json` — the app does not rely on CORS.

## Build

```powershell
npx ng build     # → dist/frontend
```

## Structure

```
src/app/
  core/
    api/            models.ts (typed DTOs), api.services.ts (MetadataApi, ControlsApi, RunsApi),
                    api-error.ts (ProblemDetails → ApiError + HTTP interceptor)
    definition/     definition.model.ts (JSON condition tree + editor tree), definition-tree.ts
                    (conversion, error paths, local checks), definition-summary.ts (Hebrew summary)
    control-draft.service.ts   hands a ready-made definition (AI, stage B) to the editor
    format.ts, labels.ts, title-strategy.ts
  shared/           field-picker, operator-select, value-input, results-table, problem-alert,
                    chips, confirm-dialog, notifier
  features/
    controls-list/  search, entity/active filters, last run, run / edit / duplicate / deactivate
    control-editor/ control-form (details, recursive condition builder, live validation, preview, save)
                    condition-builder/ (condition-group ↔ condition-rule, operand-editor, context)
    runs/           run results (paging, search, typed filters, sort, reason) + run history
    ai-create/      placeholder for stage B
```

Business terms only: fields, operators and list values come from `/api/metadata`; no table or column name appears in the code.
