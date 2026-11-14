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
(סטטוס, מספר חריגות, משך, ה-SQL שנוצר, שגיאה). `PreviewAsync` מריץ הגדרה שלא נשמרה (למשל הצעת AI לפני אישור) בלי רישום.

## פרויקטים

| פרויקט | אחריות | תלויות |
|---|---|---|
| Domain | מודל ההגדרה + Serializer, מודל ה-Metadata (`MetadataCatalog`, `ColumnInfo`), `Control` | אין |
| Application | `DefinitionBinder`, `ViolationReasonBuilder`, `ControlExecutionService`, ממשקים (`IMetadataProvider`, `IQueryBuilder`, `IControlRunner`, `IControlRepository`, `IControlRunRepository`) | Domain |
| Infrastructure | `SqlMetadataProvider` (Dapper + cache), `MetadataCatalogFactory`, `SqlQueryBuilder`, `SqlControlRunner`, repositories | Application, Dapper, Microsoft.Data.SqlClient |
| Api | רישום DI (`AddApplication`, `AddInfrastructure`). Controllers — בשלב הבא | Application, Infrastructure |

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

## בדיקות

- **Unit** (`Tests/Unit`): Serializer (כולל 14 ההגדרות מ-04), Binder (סוגים, אופרטורים, טווחים, מגבלות),
  QueryBuilder (כל אופרטור, joins, קינון, nvarchar/עברית, TRIM), Injection (ערכים, נתיבים, אופרטורים, ישויות, Metadata זדוני), סיבות.
- **Integration** (`Tests/Integration`, `Category=Integration`): 14 הבקרות מ-`ctl.Controls` מול המזהים הצפויים
  שנקראים ישירות מ-`database/99_verify.sql`; הרשאות ה-runner, REVERT, timeout, מגבלת שורות, רישום ריצה, עברית, רווחים, ישות 06.
  נדלגות אוטומטית אם ה-DB לא זמין. connection string: env `DATACONTROLS_TEST_DB` או ברירת המחדל המקומית.
