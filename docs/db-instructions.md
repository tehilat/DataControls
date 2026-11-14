# משימה: מסד נתונים לצי רחפני משלוחים, עם נתונים תקינים ושגויים

## הקשר

אנחנו בונים מערכת בקרות נתונים גנרית: משתמש עסקי מגדיר בקרה במונחים עסקיים, והמערכת מתרגמת אותה לשאילתת SQL בזמן ריצה ומחזירה את הרשומות החריגות.
המשימה הזו עוסקת **רק במסד הנתונים**: הנתונים העסקיים, שכבת ה-Metadata, טבלאות הבקרות, נתוני דוגמה והרשאות.

- **DB:** SQL Server, שם בסיס הנתונים `DroneOpsControls`
- **מיקום הסקריפטים:** תיקיית `database/` בשורש הפרויקט (התיקייה כבר קיימת וריקה)
- **שרת:** `localhost\SQLEXPRESS`, אימות Windows. שרת מקומי עם תעודה self-signed, ולכן בכל פקודת `sqlcmd` חובה להוסיף `-C`.

## לפני שמתחילים

אם בתיקייה `database/` כבר קיימים הקבצים `01` עד `06`, **אל תכתוב אותם מחדש.** במקום זה:
1. קרא אותם ובדוק שהם תואמים את המפרט כאן.
2. הרץ אותם לפי הסדר ותקן שגיאות, אם יש.
3. המשך לסעיף "אימות".

אם התיקייה ריקה, צור את הקבצים לפי המפרט.

## מבנה הקבצים

| קובץ | תוכן |
|---|---|
| `01_schema.sql` | יצירת DB, הסכמות `ops`, `meta` ו-`ctl`, וכל הטבלאות. מוחק ויוצר מחדש (DROP IF EXISTS), כך שאפשר להריץ שוב |
| `02_seed_data.sql` | נתונים עסקיים, תקינים ושגויים |
| `03_metadata.sql` | המילון העסקי: ישויות, שדות, ערכי enum, קשרים ואופרטורים |
| `04_sample_controls.sql` | 14 בקרות לדוגמה, ובראש הקובץ תיעוד מבנה ה-JSON |
| `05_security.sql` | שני משתמשי DB עם הרשאה מינימלית |
| `06_demo_add_weather_alerts.sql` | הדגמת גנריות: ישות חדשה שנוספת דרך Metadata בלבד |
| `99_verify.sql` | אימות: שאילתות שנכתבו ידנית ובודקות את התוצאות הצפויות |

## כללים לכל הסקריפטים

- קידוד **UTF-8 עם BOM**, כדי שהעברית תוצג נכון ב-SSMS.
- כל מחרוזת בעברית עם קידומת `N'...'`.
- **תאריכים יחסיים בלבד:** `DATEADD(..., @today)` / `DATEADD(..., @now)`. בלי תאריכים קבועים, כדי שהבקרות על "באיחור" ו"פג תוקף" יעבדו בכל יום שמריצים.
- מזהים מפורשים עם `SET IDENTITY_INSERT`, כדי שהתיעוד ("משלוח 9") יהיה תמיד נכון.
- **אילוצים מינימליים על נתוני `ops`:** רק PK, FK ו-CHECK על ערכי סטטוס. **לא** להוסיף אילוצים שימנעו נתונים שגויים (משקל שלילי, תאריכים הפוכים), כי המטרה היא שהבקרות יאתרו אותם.
- כל שורה שגויה מסומנת בהערה `-- [שגוי] בקרה N: <הסבר>`.
- אינדקסים על כל עמודות ה-FK.

## הסכמה `ops`: נתונים עסקיים

```
Hubs        HubId, Name, City, IsActive
Drones      DroneId, SerialNumber (unique), HubId→Hubs, Model, MaxPayloadKg, MaxRangeKm,
            LastMaintenance, NextMaintenanceDue, Status (Active | Maintenance | Retired)
Pilots      PilotId, FullName, LicenseNumber (nullable), LicenseExpiry (nullable), IsActive
Customers   CustomerId, FullName, Phone, Email, City, Address (כולם nullable חוץ מהשם)
Deliveries  DeliveryId, DroneId→Drones, PilotId→Pilots, CustomerId→Customers,
            PackageWeightKg, DistanceKm, DispatchedAt (nullable), DeliveredAt (nullable),
            Status (Pending | InTransit | Delivered | Failed)
```

### היקף הנתונים

5 מוקדים (אחד סגור), 10 רחפנים, 6 מפעילים (Pilots), 8 לקוחות וכ-22 משלוחים. ברוב הרשומות הנתונים תקינים.

### נתונים שגויים נדרשים

כל מקרה צריך להיות **מבודד**: רשומה שגויה אמורה להיתפס רק בבקרה שלה, אלא אם צוין אחרת.

| # | מקרה | ישות |
|---|---|---|
| 1 | משקל חבילה גבוה מכושר הנשיאה של הרחפן | משלוח |
| 2 | מרחק משלוח גדול מטווח הטיסה של הרחפן | משלוח |
| 3 | זמן מסירה מוקדם מזמן היציאה | משלוח |
| 4 | מפעיל שהרישיון שלו פג לפני היציאה, ומפעיל בלי רישיון בכלל | משלוח (2 רשומות) |
| 5 | סטטוס "נמסר" ללא זמן מסירה | משלוח |
| 6 | רחפן פעיל במוקד סגור | רחפן |
| 7 | רחפן בשירות (Status = Active) שמועד התחזוקה שלו עבר (2 רחפנים) | רחפן |
| 8 | לקוח בלי טלפון ובלי אימייל | לקוח |
| 9 | משלוח מעל 5 ק"ג, מעל 20 ק"מ, ברחפן עם תחזוקה באיחור | משלוח |
| 10 | סטטוס לא עקבי: "נמסר" ללא זמן מסירה, או "נכשל" עם זמן מסירה | משלוח (2 רשומות; אחת משותפת עם 5) |
| 11 | זמן יציאה עתידי | משלוח |
| 12 | משקל חבילה שלילי | משלוח |
| 13 | משלוח ברחפן שנמצא בתחזוקה | משלוח |
| 14 | משלוח ע"י מפעיל לא פעיל | משלוח |

### מקרי גבול תקינים (חשובים להוכחת דיוק)

- רחפן שמועד התחזוקה שלו עבר אבל הוא **הוצא משירות**: לא אמור להיתפס בבקרה 7.
- משלוח ברחפן עם תחזוקה באיחור, אבל קל וקצר: לא אמור להיתפס בבקרה 9.
- לקוח עם אימייל בלבד: לא אמור להיתפס בבקרה 8.
- לקוח ללא כתובת: לא מכוסה באף בקרה, ומשמש ליצירת בקרה חיה בדמו.

## הסכמה `meta`: שכבת ה-Metadata

```
Entities           EntityKey (PK), DisplayName, DisplayNamePlural, Description,
                   SchemaName, TableName, PrimaryKeyColumn, DisplayColumn, IsActive
Fields             FieldId, EntityKey→Entities, FieldKey, DisplayName, Description, ColumnName,
                   DataType (number | text | date | datetime | boolean | enum), Unit,
                   ShowInResults, SortOrder, IsActive   — unique (EntityKey, FieldKey)
FieldOptions       FieldId→Fields, OptionValue, DisplayName, SortOrder   — ערכים לשדות enum
Relations          FromEntityKey, RelationKey, ToEntityKey, FromColumn, ToColumn, DisplayName
                   — unique (FromEntityKey, RelationKey)
Operators          OperatorKey (PK), DisplayName, RequiresValue, SortOrder
DataTypeOperators  DataType, OperatorKey, DisplayNameOverride, AllowFieldCompare
```

**עקרונות:**
- כל שמות התצוגה והתיאורים **בעברית**, במונחים עסקיים.
- **הקשרים רבים-ליחיד בלבד** (מהילד להורה): משלוח ← רחפן ← מוקד, משלוח ← מפעיל, משלוח ← לקוח. כך JOIN לא משכפל רשומות.
- אופרטורים: `eq, neq, gt, gte, lt, lte, contains, isEmpty, isNotEmpty`. בתאריכים מוצגים כ"לפני" ו"אחרי" דרך `DisplayNameOverride`.
- **אסור לשמור SQL ב-meta.** התרגום ל-SQL קיים רק בקוד.

## הסכמה `ctl`: בקרות והרצות

```
Controls     ControlId, Name (unique), Description, RootEntityKey→meta.Entities,
             DefinitionJson (CHECK ISJSON), Severity (Low | Medium | High),
             Source (Manual | AI), IsActive, CreatedAt, UpdatedAt, RowVer (ROWVERSION)
ControlRuns  RunId, ControlId→Controls, StartedAt, FinishedAt, Status (Running | Succeeded | Failed),
             ViolationCount, DurationMs, ErrorMessage, GeneratedSql
```

### מבנה DefinitionJson

```
{ "version": 1, "rootEntity": "<EntityKey>", "condition": <node> }
node    = group | rule
group   = { "type": "group", "logic": "AND" | "OR", "children": [ <node>, ... ] }
rule    = { "type": "rule", "field": "<path>", "operator": "<OperatorKey>", "compareTo": <operand> }
path    = "PackageWeightKg" | "Drone.MaxPayloadKg" | "Drone.Hub.IsActive"
operand = { "kind": "value", "value": ... }
        | { "kind": "field", "field": "<path>" }
        | { "kind": "today", "offsetDays": <int> }
        | { "kind": "now" }
```

ב-`04_sample_controls.sql` יש לכתוב בקרה אחת לכל אחד מ-14 המקרים, ולציין בהערה את מספר החריגות הצפוי ואת מזהי הרשומות. בקרה 10 מדגימה קבוצות מקוננות.

## הסכמה `05_security.sql`

- `controls_app`: SELECT על `meta`, CRUD על `ctl`.
- `controls_runner`: SELECT בלבד על `ops` (ברמת הסכמה), DENY על כל השאר.
- השרת הוא SQL Server Express עם אימות Windows בלבד, ולכן **אין SQL logins וסיסמאות**. שני המשתמשים נוצרים כ-`CREATE USER ... WITHOUT LOGIN`. האפליקציה מתחברת באימות Windows, והמנוע מריץ כל בקרה בתוך `EXECUTE AS USER = 'controls_runner'` ... `REVERT`. לכן צריך לתת הרשאת `IMPERSONATE` על `controls_runner` למשתמש של האפליקציה.
- הוסף ל-`99_verify.sql` בדיקה שמוכיחה שתחת `controls_runner` פקודת `UPDATE` על `ops` ופקודת `SELECT` על `ctl` נכשלות.
- **הערה על שמות:** הטבלה העסקית נקראת `ops.Pilots` (ולא Operators) כדי לא להתבלבל עם `meta.Operators` של אופרטורי ההשוואה. בתצוגה העברית היא "מפעיל".

## `06_demo_add_weather_alerts.sql`

- טבלה חדשה `ops.WeatherAlerts`: AlertId, HubId→Hubs, Severity (Low | High | Severe), StartsAt, EndsAt, WindSpeedKmh.
- 4 התראות, מתוכן אחת שמסתיימת לפני שהתחילה ואחת עם מהירות רוח לא סבירה (מעל 200).
- רישום ב-Metadata בלבד: ישות, שדות, ערכי enum וקשר למוקד. **בלי בקרות**, כי הן יוגדרו בדמו דרך המסך.
- ניתן להרצה חוזרת (ניקוי בתחילת הסקריפט).

## אימות (חובה)

צור `99_verify.sql`, שמריץ לכל אחת מ-14 הבקרות **שאילתה שנכתבה ידנית** (לא דרך המנוע), ומחזיר טבלה:

```
ControlNumber | ControlName | ExpectedCount | ActualCount | ExpectedIds | ActualIds | Passed
```

אחר כך:
1. הרץ את `01` עד `05` ואת `99_verify.sql`, למשל:
   ```powershell
   Get-ChildItem .\database\0[1-5]*.sql | Sort-Object Name | ForEach-Object { sqlcmd -S localhost\SQLEXPRESS -E -C -b -f 65001 -i $_.FullName }
   sqlcmd -S localhost\SQLEXPRESS -E -C -f 65001 -i .\database\99_verify.sql
   ```
2. ודא שכל השורות `Passed = 1`. אם לא, תקן את הנתונים (לא את התוצאות הצפויות) והרץ שוב.
3. ודא שהעברית נשמרה נכון: `SELECT TOP 3 DisplayName FROM meta.Fields`.

הקובץ הזה ישמש בהמשך גם כבסיס לבדיקות האינטגרציה של המנוע: המנוע אמור להחזיר בדיוק את אותן תוצאות.

## הגדרת "סיום"

- [ ] כל הסקריפטים רצים מאפס בלי שגיאות, וגם בהרצה חוזרת.
- [ ] `99_verify.sql` מחזיר `Passed = 1` לכל 14 הבקרות.
- [ ] העברית מוצגת נכון בטבלאות.
- [ ] הרצת `06` מוסיפה את הישות החדשה בלי שגיאות, וגם בהרצה חוזרת.
- [ ] אין תאריכים קבועים ואין SQL שמור ב-meta.

בסיום, כתוב סיכום קצר: מה נוצר או תוקן, ותוצאת האימות.
