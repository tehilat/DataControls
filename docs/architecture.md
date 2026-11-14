# ארכיטקטורה — מנוע הבקרות (Backend)

## זרימה

```
DefinitionJson (ctl.Controls)
   │  ControlDefinitionSerializer.Parse          Domain        — JSON → מודל (group / rule / operand)
   ▼
ControlDefinition
   │  DefinitionBinder.Bind(definition, catalog) Application   — ולידציה מול ה-Metadata, שגיאות בעברית
   ▼
BoundDefinition  (שדות, קשרים ואופרטורים = אובייקטים מהקטלוג בלבד)
   │  IQueryBuilder → SqlQueryBuilder            Infrastructure — SELECT פרמטרי
   ▼
BuiltQuery (Sql + Parameters)
   │  IControlRunner → SqlControlRunner          Infrastructure — EXECUTE AS controls_runner, timeout, TOP
   ▼
RawQueryResult
   │  ViolationReasonBuilder                     Application   — סיבה בעברית לכל רשומה
   ▼
ControlRunResult (TotalCount, Truncated, Columns, Violations[Id, Values, Reason])
```

`ControlExecutionService` (Application) מתזמר את כל השלבים, ורושם כל הרצה של בקרה שמורה ב-`ctl.ControlRuns`
(סטטוס, מספר חריגות, משך, ה-SQL שנוצר, שגיאה, snapshot של עמודות התוצאה), ואת הרשומות החריגות עצמן ב-`ctl.ControlRunViolations`.
בקרה מושבתת לא רצה (409). `PreviewAsync` מריץ הגדרה שלא נשמרה (למשל הצעת AI לפני אישור) בלי רישום, ותמיד מוגבל
ל-`PreviewMaxRows` (100, ברירת מחדל `PreviewDefaultRows` = 50) — גם אם הקורא מבקש יותר.

## פרויקטים

| פרויקט | אחריות | תלויות |
|---|---|---|
| Domain | מודל ההגדרה + Serializer, מודל ה-Metadata (`MetadataCatalog`, `ColumnInfo`), `Control` | אין |
| Application | `DefinitionBinder`, `ViolationReasonBuilder`, `ControlExecutionService`, `BusinessCatalogService`, `ControlService`, `RunService`, `CanonicalValueJson`, `AiSuggestionService` + `DefinitionSummaryBuilder`, ממשקים (`IMetadataProvider`, `IQueryBuilder`, `IControlRunner`, `IControlRepository`, `IControlRunRepository`, `IAiControlGenerator`) | Domain |
| Infrastructure | `SqlMetadataProvider` (Dapper + cache), `MetadataCatalogFactory`, `SqlQueryBuilder`, `SqlControlRunner`, repositories (`SqlControlRepository`, `SqlControlRunRepository` + `TypedJsonValue`), `HttpAiControlGenerator` | Application, Dapper, Microsoft.Data.SqlClient, Microsoft.Extensions.Http |
| Api | Controllers, DTOs (`Contracts`), `ApiExceptionHandler` + `ModelStateProblems` (ProblemDetails בעברית), CORS, OpenAPI + Swagger UI | Application, Infrastructure |

## Metadata

- `SqlMetadataProvider` קורא את `meta.*` וגם את `sys.columns` של הסכמות המותרות (`Metadata:AllowedSchemas`, ברירת מחדל `ops`),
  ושומר את הקטלוג ב-`IMemoryCache` (`Metadata:CacheSeconds`, וגם `Invalidate()`).
- `MetadataCatalogFactory` פוסל (ורושם אזהרה ב-log) כל ישות/שדה/קשר ש:
  נמצא בסכמה לא מותרת, מכיל מזהה שאינו `^[A-Za-z_][A-Za-z0-9_]*$`, או מצביע על טבלה/עמודה שלא קיימת פיזית.
  כלומר ה-whitelist נבדק גם מול meta וגם מול הסכמה האמיתית.
- מ-`sys.columns` נלקחים גם סוג העמודה, precision ו-scale (`ColumnInfo`) — לצורך ולידציית טווחים.

## ולידציה (DefinitionBinder)

כל השגיאות נאספות ומוחזרות יחד (`ControlValidationException.Errors`, כל אחת עם נתיב JSON, למשל `condition.children[1].compareTo.value`):

- ישות בסיס, קשרים ושדות — רק מפתחות מה-Metadata (ללא רגישות לאותיות), בלי fallback לשמות פיזיים.
- אופרטור קיים ב-`meta.Operators`, מוכר למנוע (`ComparisonOperator`), ומותר לסוג השדה (`meta.DataTypeOperators`).
- `compareTo` חובה/אסור לפי `RequiresValue`; השוואה לשדה רק כש-`AllowFieldCompare = 1` ובסוגים תואמים (date↔datetime מותר).
- המרת ערכים לפי סוג: מספר, טקסט, תאריך (`yyyy-MM-dd`), תאריך ושעה, כן/לא, ורשימת ערכים (רק `FieldOptions`).
- **טווחים:** `offsetDays` בין ‎-36500 ל-36500; מספר חייב להתאים לעמודת היעד (למשל `DECIMAL(6,2)` → ‎±9999.99, `INT`);
  תאריך חייב להתאים ל-`datetime`/`smalldatetime` אם זה סוג העמודה. חריגה = שגיאת ולידציה בעברית, אף פעם לא שגיאת SQL.
- מגבלות: עומק 10, עד 100 תנאים, נתיב עד 5 מקטעים, טקסט עד 4000 תווים.

## בניית SQL (SqlQueryBuilder)

- כל מזהה עובר דרך `SqlIdentifier.Quote` (regex + סוגריים מרובעים) — גם אם הגיע מהקטלוג.
- כל ערך הוא פרמטר. טקסט ו-enum תמיד `DbType.String` (nvarchar, Size=4000) — עברית לעולם לא עוברת דרך varchar.
- `contains` → `LIKE @p ESCAPE N'\'`, עם escape של `% _ [ \` בצד הקוד.
- `isEmpty` על טקסט → `(c IS NULL OR TRIM(c) = N'')` — גם ערך של רווחים בלבד נחשב ריק. על סוגים אחרים → `IS NULL`.
- `today` → `DATEADD(DAY, @p, CAST(SYSDATETIME() AS DATE))`, `now` → `SYSDATETIME()` (שעון השרת).
- `LEFT JOIN` אחד לכל נתיב קשר ייחודי (`Drone`, `Drone.Hub`) לפי `meta.Relations`. הקשרים רבים-ליחיד, ולכן אין שכפול שורות.
- `SELECT TOP (@__maxRows)` + `COUNT(*) OVER ()` — מספר החריגות האמיתי גם כשהתוצאה נחתכת.
- לכל תנאי נבחרים גם הערכים שלו (`__f{i}`, `__g{i}`) ודגל (`__r{i}`), כדי לבנות את הסיבה.
- נוצר רק SELECT יחיד; אין API שמקבל SQL חיצוני.

## הרצה (SqlControlRunner)

על חיבור אחד: `EXECUTE AS USER = @runner` → השאילתה (`CommandTimeout`) → `REVERT; SELECT USER_NAME()`.
אם לא ניתן לאמת שההתחזות בוטלה (למשל אחרי ניתוק), ה-connection pool מתרוקן — חיבור מתחזה לעולם לא חוזר לשימוש.
timeout → `ControlExecutionException` עם הודעה בעברית. הגדרות: `ControlExecution:MaxRows`, `CommandTimeoutSeconds`, `RunnerUser`.

## סיבת חריגה (ViolationReasonBuilder)

- תנאי: `שם שדה (ערך בפועל) אופרטור ערך השוואה`, למשל `משקל חבילה (12.5 ק"ג) גדול מ-כושר נשיאה (רחפן) (10 ק"ג)`.
- כן/לא ורשימת ערכים: `סטטוס רחפן (רחפן): בתחזוקה`. ריק: `טלפון ריק`.
- AND — כל התנאים מוסברים ("וגם"); OR — רק התנאים שהתקיימו ("או"). סוגריים רק כשתת-קבוצה עומדת ליד אחיות.

## שכבת ה-API

כל הנתיבים תחת `/api`. מסמך OpenAPI ב-`/openapi/v1.json` ו-Swagger UI ב-`/swagger` (ב-Development).

| נתיב | תיאור |
|---|---|
| `GET metadata/entities` | ישויות עסקיות |
| `GET metadata/entities/{key}/fields?depth=2` | שדות לפי נתיב, כולל דרך קשרים (0–4), סוג, יחידה, ערכי רשימה. ישות שכבר בנתיב לא מבוקרת שוב (אין מעגלים) |
| `GET metadata/operators` | אופרטורים לפי סוג שדה (שם בעברית, `requiresValue`, `allowFieldCompare`) — רק מה שהמנוע יודע להריץ |
| `POST metadata/refresh` | ריקון ה-cache של הקטלוג |
| `GET/POST controls`, `GET/PUT/DELETE controls/{id}` | CRUD. `PUT` דורש `rowVersion` (409 אם התיישן). `DELETE` רק לבקרה שלא רצה — אחרת 409 והשבתה |
| `POST controls/{id}/activate`, `.../deactivate` | הפעלה / השבתה |
| `POST controls/validate` | בדיקת הגדרה בלי שמירה: תמיד 200 עם `isValid` ו-`errors[{path,message}]` |
| `POST controls/preview` | הרצת הגדרה שלא נשמרה, `maxRows` 1–100 (ברירת מחדל 50), בלי רישום |
| `POST controls/{id}/runs` | הרצה → 201 + `Location: /api/runs/{runId}` |
| `GET runs?controlId=&status=&page=&pageSize=` | היסטוריית הרצות, מהחדשה לישנה |
| `GET runs/{runId}` | הרצה + עמודות התוצאה (`violationCount` האמיתי, `storedCount`, `truncated`) |
| `GET runs/{runId}/violations` | רשומות חריגות: `page`, `pageSize` (1–200), `search`, `filter[key]=value` (חוזר), `sortBy`, `sortDir` |

- **קטלוג עסקי בלבד:** `BusinessCatalogService` מחזיר מפתחות, שמות בעברית, סוגים וערכי רשימה — אין בו שמות סכמה/טבלה/עמודה.
  בדיקה עוברת על כל תשובות הקטלוג (גם מול ה-DB האמיתי) ומוודאת זאת. שלב ב' (AI) ישתמש באותו שירות לבניית ה-prompt.
- **שמירת הגדרה:** הגדרה עוברת את אותו `DefinitionBinder` של ההרצה לפני שמירה, ונשמרת בצורה קנונית. כל השגיאות (שם, תיאור, הגדרה)
  מוחזרות יחד. נתיבי השגיאה יחסיים להגדרה (`condition.children[1].field`); שדות הבקשה עצמם בשמם (`name`, `rowVersion`).
- **ה-SQL שנוצר לא נחשף ב-API** (הוא מכיל שמות פיזיים). הוא נשמר ב-`ctl.ControlRuns.GeneratedSql` לצורכי תחקור בלבד.

### שמירת תוצאות, מיון וסינון לפי סוג

- `ValuesJson` נשמר בפורמט קנוני (`CanonicalValueJson`, לא תלוי culture): מספר → JSON number, תאריך → `yyyy-MM-dd`,
  תאריך ושעה → `yyyy-MM-ddTHH:mm:ss.fffffff`, כן/לא → `true`/`false`, טקסט/רשימה → מחרוזת (ערך הרשימה הגולמי). אותו פורמט מוחזר ל-client.
- `SearchText` (שם הרשומה, הסיבה והערכים בתצוגה העברית — למשל "נמסר" ולא "Delivered") משמש לחיפוש חופשי (`LIKE` עם escape).
- **מפתח** לסינון/מיון חייב להיות עמודה ב-snapshot של ההרצה (`ColumnsJson`) ולעמוד ב-regex של מזהה; אחרת 400 בעברית.
  הנתיב ב-JSON (`$."key"`) הוא תמיד פרמטר.
- **ביטוי** נבחר לפי סוג העמודה מתוך קבוצה **קבועה** של קבועים בקוד (`TypedJsonValue`) — אין SQL דינמי מהקלט:
  number → `TRY_CONVERT(decimal(38,10), JSON_VALUE(...))`, date → `TRY_CONVERT(date, ..., 23)`,
  datetime → `TRY_CONVERT(datetime2(7), ..., 126)`, אחרים → `JSON_VALUE(...)`. לכן 9 לפני 10, ותאריכים כרונולוגית.
- **ערך סינון** מפוענח ב-C# לפי הסוג (`RunService.TryParse`) ונשלח כפרמטר טיפוסי (`10` מוצא `10.00`); ערך לא תואם → 400 בעברית.
  כיוון המיון ממופה רק ל-`ASC`/`DESC`, ושובר שוויון לפי `RowNumber`.
- השמירה: `SqlBulkCopy` עם `ColumnOrderHints` (הרשומות כבר בסדר המפתח — אין sort ואין memory grant גדול ב-SQL Express),
  ובלי טרנזקציה עוטפת: נעילת שורת ההרצה בזמן שבדיקת ה-FK של ה-bulk סורקת את `ControlRuns` גרמה ל-deadlock בין הרצות מקבילות.
  ההרצה מסומנת `Succeeded` רק אחרי שהשמירה הצליחה.

### שגיאות (ProblemDetails)

| חריגה | סטטוס | `title` |
|---|---|---|
| `ControlValidationException` / `DefinitionFormatException` | 400 | הגדרת הבקרה אינה תקינה |
| `InvalidRequestException`, שגיאות model binding / DataAnnotations | 400 | הבקשה אינה תקינה |
| `NotFoundException` (בקרה, הרצה, ישות) | 404 | הפריט המבוקש לא נמצא |
| `ConflictException` (שם קיים, `rowVersion` ישן, מחיקה עם היסטוריה, הרצת בקרה מושבתת) | 409 | הפעולה מתנגשת במצב הנוכחי (+ `path`) |
| `ControlExecutionException` (timeout, הרשאה) | 422 | הרצת הבקרה נכשלה (+ `runId`) |
| כל חריגה אחרת | 500 | שגיאת שרת — הודעה כללית בלבד, בלי פרטי החריגה |

ב-400 של ולידציה: `errors` (מילון path → הודעות, הפורמט הסטנדרטי) וגם `validationErrors` — רשימה מסודרת של `{path, message}`.
לכל תשובה `traceId`. הודעות באנגלית של ה-framework (JSON שבור, enum לא חוקי) מוחלפות בהודעות בעברית עם שם השדה.

**CORS:** מדיניות `Frontend` מ-`Cors:AllowedOrigins` (ברירת מחדל `http://localhost:4200`), חושפת את `Location`.
`UseHttpsRedirection` רק מחוץ ל-Development, כדי לא לשבור preflight מה-dev server של Angular.

## שכבת ה-AI (שלב ב')

משתמש מתאר בקרה בשפה חופשית, וה-AI **מציע** הגדרה במבנה DefinitionJson של שלב א'. ההצעה עוברת בדיוק את אותה ולידציה
כמו הגדרה ידנית, ומוצגת למשתמש לאישור. **שום דבר לא נשמר ולא רץ** עד שהמשתמש מאשר.

### זרימה

```
POST /api/ai/suggest-control {text}                       Api           — AiController
   │  AiSuggestionService.SuggestAsync                     Application   — בדיקת הטקסט (1–2000 תווים)
   │    ├─ BuildCatalogAsync ← IBusinessCatalogService       קטלוג עסקי: ישויות, נתיבי שדות (עומק 2), סוגים, אופרטורים, ערכי enum
   │    └─ IAiControlGenerator.GenerateAsync(text, catalog)
   ▼
HttpAiControlGenerator                                     Infrastructure — POST {AiService:BaseUrl}/generate-control
   ▼
ai-service (Python, FastAPI)                               PromptBuilder → AiProvider (Mock) → ולידציה מול הקטלוג
   ▼  {definition, explanation, confidence, warnings}      definition = JSON לא מהימן
AiSuggestionService                                        ControlDefinitionSerializer.Parse → DefinitionBinder.Bind
   │                                                       → DefinitionSummaryBuilder (תקציר בעברית מהקטלוג)
   ▼
AiSuggestionDto  → המשתמש בודק, עורך, ואז שומר דרך POST /api/controls עם source = "AI"
                   (או מנסה קודם דרך POST /api/controls/preview)
```

- **ה-AI רואה רק מונחים עסקיים.** הקטלוג נבנה מ-`IBusinessCatalogService` (אותו שירות שמזין את ה-UI), עם נתיבים שכבר
  "פורקו" דרך הקשרים (`Drone.Hub.IsActive`). אין בו שמות סכמה, טבלה או עמודה. שירות ה-Python לא ניגש ל-DB.
- **ה-.NET לא סומך על התשובה.** הולידציה ב-Python היא קו ראשון בלבד. ההחלטה הסופית היא של אותו `DefinitionBinder` שבודק
  הגדרות ידניות, כולל מה שה-Python לא יודע (למשל טווחי ערכים לפי `sys.columns`, `DECIMAL(6,2)` → ‎±9999.99).
- **הפרדה מבנית:** ל-`AiSuggestionService` יש רק ארבע תלויות: `IAiControlGenerator`, `IBusinessCatalogService`,
  `IMetadataProvider` ו-`DefinitionBinder`. אין לו גישה ל-repository או ל-runner, ולכן הוא לא יכול לשמור או להריץ (יש לכך גם בדיקה).
- `GET /api/ai/prompt-preview?text=...` מחזיר את ה-prompt המלא ששירות ה-Python בונה (system, user, JSON schema של התשובה),
  להדגמה שהתשתית מוכנה למודל אמיתי.

### מבנה התשובה (`AiSuggestionDto`)

| שדה | משמעות |
|---|---|
| `definition` | ההגדרה המוצעת (DefinitionJson). כשהיא תקינה, בצורה קנונית. כשאינה תקינה, כפי שהתקבלה, כדי שאפשר יהיה לתקן אותה בעורך. `null` כשה-AI לא הצליח לתרגם |
| `isValid` | האם ההגדרה עברה את ה-Binder |
| `rootEntity`, `rootEntityDisplayName` | ישות הבסיס (כשתקינה) |
| `summary` | תקציר בעברית שנבנה **מהקטלוג**, כלומר מה שבאמת ירוץ, למשל `יאותרו רחפנים שבהם סטטוס רחפן: בשירות וגם מוקד פעיל (מוקד): לא.` |
| `explanation` | ההסבר של ה-AI עצמו |
| `confidence` | 0–1 כפי שה-AI דיווח; 0 כשאין הגדרה או שהיא לא תקינה |
| `warnings` | הנחות והערות של ה-AI (למשל איך פורש "באיחור") |
| `validationErrors` | `[{path, message}]`, אותן הודעות כמו בולידציה ידנית |

בקשה שלא זוהתה **אינה שגיאה**: מוחזר 200 עם `definition: null` והסבר ב-`warnings`.
טקסט ריק או ארוך מדי מחזיר 400. שירות AI לא זמין (לא רץ, timeout, סטטוס שגיאה, תשובה לא תקינה) מחזיר **503** ProblemDetails
בעברית (`AiServiceUnavailableException`). שאר ה-API ממשיך לעבוד, ואפשר להגדיר בקרות ידנית.

הגדרות: `AiService:BaseUrl` (ברירת מחדל `http://localhost:8001`) ו-`AiService:TimeoutSeconds` (30).
גודל תשובה מרבי: 1MB.

### החלפת מודל

הבחירה במודל נעשית כולה בשירות ה-Python. כדי לחבר מודל אמיתי:

1. כותבים מחלקה חדשה ב-`ai-service/providers/` שמממשת את `AiProvider.generate`: שולחת את `prompt.system` ואת `prompt.user`
   למודל, עם `prompt.response_schema` כ-structured output / JSON mode, ומחזירה `ProviderResult`.
   בכשל רשת או מכסה זורקים `ProviderError`, שהופך ל-502 ב-Python ול-503 ב-.NET.
2. רושמים אותה ב-`_PROVIDERS` (`providers/__init__.py`) ומגדירים `AI_PROVIDER=<name>`.

**אין שום שינוי ב-.NET:** החוזה (`{text, catalog}` → `{definition, explanation, confidence, warnings}`), הולידציה, ה-API,
מנוע הבקרות וה-UI נשארים כמו שהם. אפשר גם להחליף את השירות כולו (למשל שירות מנוהל) דרך מימוש אחר של `IAiControlGenerator`.

## הנחות ומגבלות

- **אין אימות משתמשים והרשאות.** ה-API פתוח לכל מי שמגיע אליו, ולכן מיועד לרשת מקומית/פנימית בלבד; לא נרשם מי יצר או שינה בקרה.
  הרחבה אפשרית: JWT / Windows Authentication ותפקידים (מגדיר בקרות מול צופה בתוצאות), ועמודות `CreatedBy`/`UpdatedBy`.
- **VIEW DEFINITION.** ה-Metadata provider קורא את `sys.columns` של `ops` כדי לאמת את ה-whitelist ואת טווחי הערכים.
  מקומית האפליקציה מתחברת ב-Windows Authentication כ-dbo ולכן זה עובד. משתמש אפליקציה עם הרשאות מינימליות (`controls_app`)
  יצטרך `GRANT VIEW DEFINITION ON SCHEMA::ops TO controls_app` — בלעדיה SQL Server מסתיר את העמודות, וכל השדות ייפסלו
  מהקטלוג כאילו העמודות לא קיימות.
- **שמות פיזיים באנגלית בלבד.** שמות סכמות, טבלאות ועמודות חייבים לעמוד ב-`^[A-Za-z_][A-Za-z0-9_]*$`; ישות/שדה/קשר עם שם אחר
  נפסלים מהקטלוג (עם אזהרה ב-log). עברית קיימת רק בשמות התצוגה. גם מפתחות שדה (`FieldKey`) שמשמשים לסינון/מיון בתוצאות
  חייבים לעמוד באותו regex.
- **מדיניות שמירה להרצות — הרחבה עתידית.** כל הרצה ותוצאותיה (עד `MaxRows` רשומות) נשמרות ללא הגבלת זמן.
  מתוכנן: `ControlExecution:RunRetentionDays` וניקוי תקופתי, או שמירת N ההרצות האחרונות לכל בקרה. מחיקת הרצה מוחקת גם את
  תוצאותיה (`ON DELETE CASCADE`).
- `DateTimeOffset` נשמר בתוצאות לפי התאריך והשעה המקומיים שלו (ה-offset לא נשמר); עמודות `ops` הן `datetime2`.
- הרצה שהתהליך נפל באמצעה נשארת בסטטוס `Running`.

## Frontend — "רדאר - בקרות איכות נתונים"

Angular 22 (zoneless, standalone, signals) + Angular Material. עברית בלבד, `dir="rtl"`, `lang="he"`. כל הקריאות ל-`/api`
יחסיות ועוברות דרך `proxy.conf.json` של שרת הפיתוח.

| שכבה | תוכן |
|---|---|
| `core/api` | DTOs מוקלדים, שירותי API (הקטלוג נשמר ב-cache לסשן), `apiErrorInterceptor` → `ApiError` מתוך ProblemDetails |
| `core/definition` | מודל ההגדרה (JSON) ועץ עריכה עם מזהים; המרה דו-כיוונית, בדיקות שלמות מקומיות עם אותם נתיבי JSON של השרת, תקציר בעברית |
| `shared` | `field-picker`, `operator-select`, `value-input`, `results-table`, `problem-alert`, chips, דיאלוג אישור |
| `features` | רשימת בקרות, עורך (`ControlForm` + בונה תנאים רקורסיבי), תוצאות הרצה, היסטוריה, placeholder ל-AI |

- **הגנריות נשמרת:** שדות, אופרטורים, יחידות וערכי רשימה מגיעים מ-`/api/metadata`; עמודות התוצאה מגיעות מההרצה. אין שמות טבלה/עמודה בקוד.
- **ולידציה:** בדיקות שלמות מקומיות (שדה/תנאי/ערך חסרים, קבוצה ריקה, עומק, מספר תנאים) — ורק הגדרה שלמה נשלחת
  ל-`controls/validate` (debounce). שגיאות השרת מוצגות על התנאי עצמו לפי הנתיב, וגם ברשימה עם מיקום קריא ("תנאי 2 › 1").
- **Preview** מריץ את ההגדרה הנוכחית בלי לשמור (עד 50 רשומות) ומסמן כשההגדרה השתנתה מאז.
- **שלב ב':** `ControlForm` מקבל `seed` (הגדרה מוכנה). מסך ה-AI יעביר את ההצעה דרך `ControlDraftService` ל-`/controls/new?draft=1`,
  והמשתמש יבדוק, יריץ preview ויאשר לפני שמירה (`source: AI`).
- **מגבלות:** אין אימות משתמשים (כמו ב-API); תאריכים מוצגים כפי שהשרת שומר אותם (שעון השרת, בלי המרת אזור זמן).

## בדיקות

- **Unit** (`Tests/Unit`): Serializer (כולל 14 ההגדרות מ-04), Binder (סוגים, אופרטורים, טווחים, מגבלות),
  QueryBuilder (כל אופרטור, joins, קינון, nvarchar/עברית, TRIM), Injection (ערכים, נתיבים, אופרטורים, ישויות, Metadata זדוני), סיבות.
- **Integration** (`Tests/Integration`, `Category=Integration`): 14 הבקרות מ-`ctl.Controls` מול המזהים הצפויים
  שנקראים ישירות מ-`database/99_verify.sql`; הרשאות ה-runner, REVERT, timeout, מגבלת שורות, רישום ריצה, עברית, רווחים, ישות 06.
  נדלגות אוטומטית אם ה-DB לא זמין. connection string: env `DATACONTROLS_TEST_DB` או ברירת המחדל המקומית.
- **API** (`Tests/Api`, `WebApplicationFactory<Program>`):
  - `CatalogOnlyApiFactory` — הקטלוג בזיכרון (`TestCatalog`) ו-connection string שלא מגיע לשום שרת. בדיקות הקטלוג
    (נתיבים, עומק, מעגלים, אופרטורים, אין שמות פיזיים), validate, פורמט השגיאות, 500 בלי דליפה, CORS ו-OpenAPI רצות בלי DB.
  - `ApiFactory` (`Category=Integration`) — CRUD מלא, `rowVersion`, שם כפול, מחיקה עם היסטוריה, בקרה מושבתת, הרצה מול
    `99_verify.sql`, paging, חיפוש, סינון, ניסיונות injection, ומיון/סינון טיפוסי על הרצה סינתטית דטרמיניסטית
    (מספרים 2.5 < 9 < 10 < 100, תאריכים ותאריך-ושעה, `10` מוצא `10.00`). כל בקרה שנוצרת נמחקת בסוף.
- **Unit:** `CanonicalValueJson` תחת cultures שונים (he-IL, de-DE, ar-SA), מגבלת ה-preview (50/100) בלי רישום הרצה.
- **AI:**
  - `AiSuggestionServiceTests` עם generator מזויף: הצעה תקינה (קנונית, עם תקציר), שגיאות Binder (כולל טווח עמודה),
    JSON פגום, טקסט ריק או ארוך, קטלוג בלי שמות פיזיים, ובדיקה שאין לשירות תלות שמאפשרת לשמור או להריץ.
  - `HttpAiControlGeneratorTests` עם `HttpMessageHandler` מזויף: מבנה הבקשה (חוזה ה-Python), פענוח התשובה,
    סטטוס שגיאה, ניתוק, timeout ותשובה פגומה הופכים ל-503; ביטול של הקורא לא הופך ל-503.
  - `AiApiTests`: ה-API עם generator מזויף, ו-`AiServiceDownApiTests` עם ה-generator האמיתי מול פורט סגור (503,
    ושאר ה-API ממשיך לעבוד).
  - בדיקות ה-Python: `cd ai-service && pytest`.
