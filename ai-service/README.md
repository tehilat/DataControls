# AI service — free text → control definition (stage B)

A Python FastAPI service that translates a control described in free Hebrew text into a control definition
(`DefinitionJson`, the same format as stage A: `docs/db-instructions.md`).
No real AI call is made: the active provider is a **Mock** with canned answers. The infrastructure is ready for a real model.

The service **does not access the DB**. The .NET API sends the business catalog with every request.
The service only sees business terms (entity keys, field paths, Hebrew names), never tables, columns or SQL.

## Running

```powershell
cd ai-service
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements-dev.txt
uvicorn main:app --reload --port 8001      # Swagger: http://localhost:8001/docs
pytest                                      # tests
```

Settings (environment variables): `AI_PROVIDER` (default `mock`, which needs no API key).

**A wrong provider or a missing API key does not crash the service.** It starts "not configured" and logs the reason;
`/health` returns 503 `{"status":"not_configured","provider":...,"detail":...}` and the other endpoints return
503 problem+json with the same Hebrew `detail` (unknown provider, or which environment variable is missing).
The .NET API passes that `detail` to the user, so the "create with AI" screen says what to fix, and the rest of the system keeps working.

## Endpoints

| Method | Path | Description |
|---|---|---|
| GET | `/health` | `{"status":"ok","provider":"mock"}`, or 503 `{"status":"not_configured",...}` |
| POST | `/generate-control` | `{text, catalog}` → `{definition, explanation, confidence, warnings}` |
| POST | `/prompt-preview` | `{text, catalog}` → `{provider, system, user, responseSchema}`: the full prompt a real model would receive |

Request example: `samples/generate-request.json`, from the `ai-service` folder:

```powershell
curl.exe -X POST http://localhost:8001/generate-control -H "Content-Type: application/json" --data-binary "@samples/generate-request.json"
```

In Windows PowerShell, `curl` is an alias of `Invoke-WebRequest` and does not accept these options, so use `curl.exe`
(the real curl, included in Windows 10+). The quotes around `@samples/...` stop PowerShell from treating `@` as splatting.

Errors: 422 for an invalid request, 502 when the provider fails, 503 when the service is not configured.
All return `application/problem+json`.
A request that is not recognized is **not** an error. It returns 200 with `definition: null`, `confidence: 0` and a Hebrew explanation in `warnings`.

## The catalog (contract with .NET)

```jsonc
{
  "version": 1,
  "entities": [{
    "key": "Delivery", "displayName": "משלוח", "displayNamePlural": "משלוחים", "description": "...",
    "fields": [   // every path reachable from the entity, including through relations
      { "path": "PackageWeightKg",    "displayName": "משקל חבילה",          "dataType": "number", "unit": "ק\"ג" },
      { "path": "Drone.MaxPayloadKg", "displayName": "כושר נשיאה (רחפן)",   "dataType": "number" },
      { "path": "Status", "displayName": "סטטוס משלוח", "dataType": "enum",
        "options": [{ "value": "Delivered", "displayName": "נמסר" }] }
    ]
  }],
  "operators":     [{ "key": "gt", "displayName": "גדול מ", "requiresValue": true }],
  "typeOperators": [{ "dataType": "date", "operator": "lt", "displayName": "לפני", "allowFieldCompare": true }]
}
```

This maps 1:1 to `meta.Entities/Fields/FieldOptions/Relations/Operators/DataTypeOperators`. The relations are already
flattened into paths, so the model (and the service) do not need to know how to navigate relations.

## Structure

```
main.py                  FastAPI: endpoints, DI of the provider, ProblemDetails
schemas.py               Pydantic: Catalog, ControlDefinition (group/rule/operand), Request/Response
service.py               ControlGenerationService: prompt → provider → validation
prompt_builder.py        PromptBuilder: system prompt from the catalog + JSON schema of the answer
definition_validator.py  validation of the definition against the catalog (paths, operators, types, enum values, limits)
providers/
  base.py                AiProvider (interface), ProviderRequest/Result, ProviderError
  mock_provider.py       MockProvider: recognition by keyword groups
  mock_scenarios.json    the canned answers (data, not code)
tests/                   pytest (catalog_factory.py builds the catalog the way .NET would, from 03_metadata)
```

## The flow and the safety principle

1. `PromptBuilder` builds the prompt from the catalog alone (no domain term in the code). The user's text is wrapped in
   `<request>` tags and marked as data rather than instructions. The answer format is the JSON schema of `GenerateResponse`.
2. The provider returns a **raw** answer (untrusted JSON).
3. The service validates every answer, from the mock or from a model, the same way:
   the `DefinitionJson` schema (extra fields are rejected), then `definition_validator` against the catalog:
   root entity, paths, operator allowed for the type, `compareTo` required or forbidden, field comparison only where
   `allowFieldCompare` is set, value types, enum values, `today`/`now` only on dates, limits (depth 10, 100 conditions).
   Keys are case-insensitive and are returned in their canonical spelling.
4. An invalid answer is not returned as a definition: `definition: null`, `confidence: 0` and the reasons in `warnings`.

The .NET `DefinitionBinder` remains the final validation before saving or running. The generated control is shown to
the user for approval, and is saved with `Source = 'AI'` only after approval.

## Mock

Recognition is based on keyword groups. Every group must match, and any term within a group is enough.
The most specific scenario (the one with the most groups) wins. The text is normalized first: niqqud, quote marks
(`דוא"ל`) and Hebrew final letters (`רחפן`/`רחפנים`) are unified.

| Request example | Root entity | Definition |
|---|---|---|
| משקל מעל כושר נשיאה | Delivery | `PackageWeightKg > Drone.MaxPayloadKg` |
| רחפן פעיל במוקד סגור | Drone | `Status = Active AND Hub.IsActive = false` |
| לקוח בלי טלפון ובלי אימייל | Customer | `Phone isEmpty AND Email isEmpty` |
| משלוח באיחור | Delivery | `Status = InTransit AND DispatchedAt < today-1` (with an assumption in warnings, confidence 0.6) |
| מפעיל עם רישיון שפג | Pilot | `LicenseExpiry < today` |
| משלוחים של מפעיל שהרישיון שלו פג | Delivery | `Pilot.LicenseExpiry < DispatchedAt` |
| משקל חבילה שלילי | Delivery | `PackageWeightKg < 0` |

Adding a scenario means editing `mock_scenarios.json` only. A test verifies that every scenario is valid against the catalog.

## Replacing with a real model

1. Add a class in `providers/` that implements `AiProvider.generate`: send `request.prompt.system` and `request.prompt.user`
   to the model, with `request.prompt.response_schema` as a structured output / JSON mode, and return a `ProviderResult`.
   Raise `ProviderError` on network or quota failures.
   Declare what it needs in `required_env`, e.g. `required_env = ("OPENAI_API_KEY",)`, and read the key from the environment
   (never from a file in the repository). If it is missing or blank, the service starts "not configured" with a message
   naming the variable, instead of failing.
2. Register it in `_PROVIDERS` in `providers/__init__.py` and set `AI_PROVIDER=<name>`.

No change is needed in the validation, in the API, in .NET or in the control engine.
