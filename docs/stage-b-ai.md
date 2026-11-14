# שלב ב' - הגדרת בקרות בשפה חופשית (AI)

משתמש עסקי מתאר בקרה במילים, למשל "רחפן פעיל במוקד סגור". המערכת מציעה הגדרת בקרה **באותו מבנה JSON של שלב א'**,
מציגה אותה לאישור, ורק אחרי אישור היא נשמרת ואפשר להריץ אותה. אין קריאה לשירות AI חיצוני: הספק הפעיל הוא Mock,
והתשתית מוכנה להחלפה במודל אמיתי בלי שינוי ב-.NET.

## מיפוי לדרישות

| דרישה | מימוש |
|---|---|
| ממשק להזנת תיאור בשפה חופשית | `POST /api/ai/suggest-control {text}`: נקודת הכניסה של ממשק המשתמש |
| העברת מידע על מבנה הנתונים ומשמעותם למודל | הקטלוג העסקי (ישויות, נתיבי שדות, סוגים, יחידות, אופרטורים, ערכי רשימה, תיאורים) נשלח עם כל בקשה. `PromptBuilder` בונה ממנו prompt מלא |
| הגדרת מבנה התשובה המצופה | `GenerateResponse` ב-`ai-service/schemas.py`: `{definition, explanation, confidence, warnings}`. ה-JSON schema שלו נשלח למודל כחלק מה-prompt |
| קליטת תשובת AI באמצעות Mock | `MockProvider`, עם תשובות מוכנות ב-`mock_scenarios.json` |
| תרגום התשובה להגדרת בקרה של שלב א' | `definition` הוא DefinitionJson. ה-.NET מפענח אותו עם `ControlDefinitionSerializer` ומאמת עם אותו `DefinitionBinder` של הגדרה ידנית |
| הצגה לאישור לפני הרצה | `AiSuggestionService` לא שומר ולא מריץ. המשתמש בודק, עורך ושומר דרך `POST /api/controls` עם `source: "AI"` |
| הפרדה בין ה-AI למנוע הבקרות | שירות Python נפרד מאחורי `IAiControlGenerator`. מנוע הבקרות לא יודע מאיפה הגיעה ההגדרה |

## הזרימה

```
משתמש: "רחפן פעיל במוקד סגור"
   │
   ▼  POST /api/ai/suggest-control {text}
.NET API ─ AiSuggestionService
   │   1. בונה קטלוג עסקי מ-IBusinessCatalogService (רק מונחים עסקיים, בלי שמות טבלאות/עמודות)
   │   2. IAiControlGenerator → HttpAiControlGenerator
   ▼  POST http://localhost:8001/generate-control {text, catalog}
Python ai-service
   │   3. PromptBuilder: system prompt (תפקיד, קטלוג, אופרטורים, כללים, דוגמה, JSON schema) + user prompt
   │   4. AiProvider.generate → MockProvider (או מודל אמיתי בעתיד)
   │   5. ולידציה ראשונה: מבנה DefinitionJson + נתיבים/אופרטורים/ערכים מהקטלוג בלבד
   ▼  {definition, explanation, confidence, warnings}
.NET API ─ AiSuggestionService
   │   6. ControlDefinitionSerializer.Parse + DefinitionBinder.Bind (הולידציה הסופית, כולל טווחי ערכים לפי העמודות)
   │   7. DefinitionSummaryBuilder: תקציר בעברית שנבנה מהקטלוג (מה שבאמת ירוץ)
   ▼  AiSuggestionDto
משתמש: בודק את התקציר, ההסבר וההנחות ← (אופציונלי) תצוגה מקדימה ← עריכה ← שמירה (source = AI) ← הרצה
```

**עקרון בטיחות:** תשובת ה-AI היא JSON לא מהימן. היא עוברת שתי שכבות ולידציה (Python ואז .NET), ובשום שלב לא נוצר ממנה SQL
ישירות. ה-SQL נוצר רק מהגדרה שעברה את ה-Binder, בדיוק כמו בבקרה ידנית. גם מודל שמחזיר "שדה" כמו
`PackageWeightKg; DROP TABLE` או `"type": "sql"` נדחה בשלב הוולידציה.

## מבנה הבקשה והתשובה

### API של ה-.NET (מה שה-Frontend קורא)

**בקשה:**
```json
POST /api/ai/suggest-control
{ "text": "רחפן פעיל במוקד סגור" }
```

**תשובה (200):**
```json
{
  "definition": {
    "version": 1, "rootEntity": "Drone",
    "condition": { "type": "group", "logic": "AND", "children": [
      { "type": "rule", "field": "Status", "operator": "eq", "compareTo": { "kind": "value", "value": "Active" } },
      { "type": "rule", "field": "Hub.IsActive", "operator": "eq", "compareTo": { "kind": "value", "value": false } } ] }
  },
  "isValid": true,
  "rootEntity": "Drone",
  "rootEntityDisplayName": "רחפן",
  "summary": "יאותרו רחפנים שבהם סטטוס רחפן: בשירות וגם מוקד פעיל (מוקד): לא.",
  "explanation": "הבקרה תאתר רחפנים בסטטוס \"בשירות\" שמשויכים למוקד שאינו פעיל.",
  "confidence": 0.9,
  "warnings": [ "\"רחפן פעיל\" פורש כרחפן בסטטוס \"בשירות\"." ],
  "validationErrors": []
}
```

| שדה | משמעות |
|---|---|
| `definition` | ההגדרה המוצעת. `null` כשה-AI לא הצליח לתרגם את הבקשה |
| `isValid` | האם ההגדרה עברה את ה-Binder. אם לא, היא מוחזרת כפי שהתקבלה, כדי שאפשר יהיה לתקן אותה בעורך |
| `summary` | תקציר בעברית שה-.NET בונה מהקטלוג, ולא ה-AI. הוא מתאר את מה שבאמת ירוץ |
| `explanation` | ההסבר של ה-AI |
| `confidence` | 0–1. מוחזר 0 כשאין הגדרה או שהיא לא תקינה |
| `warnings` | ההנחות שה-AI הניח. המשתמש צריך לראות אותן לפני האישור |
| `validationErrors` | `[{path, message}]`, אותן הודעות כמו בולידציה ידנית |

| מצב | סטטוס |
|---|---|
| הצעה (תקינה או לא) | 200 |
| בקשה לא מזוהה | 200, עם `definition: null` והסבר ב-`warnings` |
| טקסט ריק או ארוך מ-2000 תווים | 400 ProblemDetails |
| שירות ה-AI לא זמין, timeout או תשובה פגומה | 503 ProblemDetails בעברית. שאר המערכת ממשיכה לעבוד |

### החוזה בין ה-.NET לשירות ה-Python

```json
POST http://localhost:8001/generate-control
{
  "text": "רחפן פעיל במוקד סגור",
  "catalog": {
    "version": 1,
    "entities": [{
      "key": "Drone", "displayName": "רחפן", "displayNamePlural": "רחפנים",
      "fields": [
        { "path": "Status", "displayName": "סטטוס רחפן", "dataType": "enum",
          "options": [{ "value": "Active", "displayName": "בשירות" }, { "value": "Maintenance", "displayName": "בתחזוקה" }] },
        { "path": "Hub.IsActive", "displayName": "מוקד פעיל (מוקד)", "dataType": "boolean" }
      ]
    }],
    "operators":     [{ "key": "eq", "displayName": "שווה ל", "requiresValue": true }],
    "typeOperators": [{ "dataType": "boolean", "operator": "eq", "displayName": "הוא", "allowFieldCompare": false }]
  }
}
```

התשובה: `{definition, explanation, confidence, warnings}`. השירות לא ניגש ל-DB.
פירוט מלא נמצא ב-[ai-service/README.md](../ai-service/README.md).

## 5 הבקשות שה-Mock מזהה

ה-Mock מזהה בקשות לפי קבוצות מילות מפתח. הטקסט מנורמל קודם: ניקוד, מרכאות ואותיות סופיות (`רחפן`/`רחפנים`) מאוחדים,
כך שגם ניסוחים שונים מזוהים, למשל "לקוחות ללא טלפון וללא דוא"ל" או "טייסים שהרשיון שלהם לא בתוקף".
התוצאות בטבלה נבדקו דרך `POST /api/controls/preview` מול נתוני הדוגמה.

| # | בקשה | ישות | תקציר (מה-.NET) | ביטחון | חריגות בנתוני הדוגמה |
|---|---|---|---|---|---|
| 1 | משקל מעל כושר נשיאה | משלוח | יאותרו משלוחים שבהם משקל חבילה גדול מ-כושר נשיאה (רחפן). | 0.92 | משלוח 10 |
| 2 | רחפן פעיל במוקד סגור | רחפן | יאותרו רחפנים שבהם סטטוס רחפן: בשירות וגם מוקד פעיל (מוקד): לא. | 0.9 | רחפן 9 |
| 3 | לקוח בלי טלפון ובלי אימייל | לקוח | יאותרו לקוחות שבהם טלפון ריק וגם אימייל ריק. | 0.95 | לקוח 6 |
| 4 | משלוח באיחור | משלוח | יאותרו משלוחים שבהם סטטוס משלוח: בדרך וגם זמן יציאה לפני היום - 1 ימים. | 0.6 | אין (0) |
| 5 | מפעיל עם רישיון שפג | מפעיל | יאותרו מפעילים שבהם תוקף רישיון לפני היום. | 0.88 | מפעיל 5 |

הערות:
- **"משלוח באיחור"** מדגים בקשה עמומה. בנתונים אין מועד מסירה מתוכנן, ולכן ה-Mock מפרש "באיחור" כמשלוח שנמצא
  "בדרך" יותר מיממה. הוא מסמן את ההנחה ב-`warnings` ומחזיר ביטחון נמוך (0.6). בנתוני הדוגמה אין משלוח כזה, ולכן
  התוצאה היא 0 חריגות. זו בדיוק הסיבה שהמשתמש מאשר ויכול לערוך את ההצעה לפני השמירה.
- **"מפעיל עם רישיון שפג"** מחזיר בקרה על ישות "מפעיל". הבקשה "משלוחים שבוצעו ע"י מפעיל שהרישיון שלו פג" מזוהה
  כבקשה אחרת, ספציפית יותר, על ישות "משלוח" (תוקף הרישיון לפני זמן היציאה).
- ה-Mock מזהה גם "משלוחים עם משקל חבילה שלילי". כל התרחישים מוגדרים כנתונים ב-`ai-service/providers/mock_scenarios.json`.
- בקשה לא מזוהה ("תביא לי פיצה") מחזירה `definition: null`, `confidence: 0`, ודוגמאות לבקשות נתמכות ב-`warnings`.

## דוגמאות curl

התנאים: ה-API רץ על http://localhost:5149 ושירות ה-AI רץ על http://localhost:8001 (ראו [README](../README.md)).

> **קידוד:** ב-Windows, טקסט עברי שמועבר ישירות בשורת הפקודה עלול להשתבש לפני שהוא מגיע ל-curl. זה קורה גם ב-PowerShell 5.1
> וגם ב-Git Bash. לכן גוף הבקשות שמור כקבצי UTF-8 ב-[docs/requests/](requests/), ו-curl שולח אותם עם `--data-binary @file`.
> ב-PowerShell משתמשים ב-`curl.exe` (ולא `curl`, שהוא כינוי ל-`Invoke-WebRequest`).
> כדי שהעברית בתשובה תוצג נכון בקונסול: `[Console]::OutputEncoding = [Text.Encoding]::UTF8`.

מתוך תיקיית השורש של הפרויקט:

```powershell
# 1. משקל מעל כושר נשיאה
curl.exe -s -X POST http://localhost:5149/api/ai/suggest-control -H "Content-Type: application/json" --data-binary "@docs/requests/1-weight-over-payload.json"

# 2. רחפן פעיל במוקד סגור
curl.exe -s -X POST http://localhost:5149/api/ai/suggest-control -H "Content-Type: application/json" --data-binary "@docs/requests/2-active-drone-closed-hub.json"

# 3. לקוח בלי טלפון ובלי אימייל
curl.exe -s -X POST http://localhost:5149/api/ai/suggest-control -H "Content-Type: application/json" --data-binary "@docs/requests/3-customer-without-contact.json"

# 4. משלוח באיחור
curl.exe -s -X POST http://localhost:5149/api/ai/suggest-control -H "Content-Type: application/json" --data-binary "@docs/requests/4-late-delivery.json"

# 5. מפעיל עם רישיון שפג
curl.exe -s -X POST http://localhost:5149/api/ai/suggest-control -H "Content-Type: application/json" --data-binary "@docs/requests/5-pilot-expired-license.json"

# בקשה לא מזוהה
curl.exe -s -X POST http://localhost:5149/api/ai/suggest-control -H "Content-Type: application/json" --data-binary "@docs/requests/6-unrecognized.json"
```

חלופה ב-PowerShell, עם כל טקסט שרוצים, בלי קובץ:

```powershell
function Suggest-Control([string]$Text) {
    $body = [Text.Encoding]::UTF8.GetBytes((@{ text = $Text } | ConvertTo-Json -Compress))
    Invoke-RestMethod -Method Post -Uri http://localhost:5149/api/ai/suggest-control `
        -ContentType 'application/json; charset=utf-8' -Body $body
}
Suggest-Control 'רחפן פעיל במוקד סגור' | ConvertTo-Json -Depth 20
```

### מאישור ועד הרצה

```powershell
$s = Suggest-Control 'לקוח בלי טלפון ובלי אימייל'

# תצוגה מקדימה, בלי שמירה ובלי רישום הרצה (עד 100 רשומות)
$preview = @{ definition = $s.definition } | ConvertTo-Json -Depth 20 -Compress
Invoke-RestMethod -Method Post -Uri http://localhost:5149/api/controls/preview `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($preview))

# שמירה אחרי אישור המשתמש, עם מקור "AI"
$save = @{ name = 'לקוח ללא פרטי קשר (AI)'; description = $s.explanation; severity = 'Low'; source = 'AI'; definition = $s.definition } |
    ConvertTo-Json -Depth 20 -Compress
Invoke-RestMethod -Method Post -Uri http://localhost:5149/api/controls `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($save))
```

### ה-prompt שמודל אמיתי היה מקבל

```powershell
curl.exe -s "http://localhost:5149/api/ai/prompt-preview?text=%D7%9E%D7%A9%D7%9C%D7%95%D7%97%20%D7%91%D7%90%D7%99%D7%97%D7%95%D7%A8"
```

(הטקסט ב-URL הוא "משלוח באיחור", מקודד ב-URL.)
התשובה כוללת:
- `system`: תפקיד, הקטלוג המלא, אופרטורים לפי סוג, כללים, דוגמה שנגזרת מהקטלוג, ו-JSON schema של התשובה.
- `user`: הבקשה, עטופה בתגיות `<request>` ומסומנת כנתונים ולא כהוראות.
- `responseSchema`: ה-schema של התשובה, לשימוש כ-structured output.

### ישירות מול שירות ה-Python

ה-Swagger של השירות זמין ב-http://localhost:8001/docs. בקשה מלאה כולל קטלוג נמצאת ב-`ai-service/samples/generate-request.json`:

```powershell
curl.exe -s -X POST http://localhost:8001/generate-control -H "Content-Type: application/json" --data-binary "@ai-service/samples/generate-request.json"
```

## החלפה במודל אמיתי

1. מוסיפים מחלקה ב-`ai-service/providers/` שמממשת את `AiProvider.generate`. היא שולחת את `prompt.system` ואת `prompt.user`
   למודל, עם `prompt.response_schema` כ-structured output / JSON mode, ומחזירה `ProviderResult`.
2. רושמים אותה ב-`_PROVIDERS` ומגדירים `AI_PROVIDER=<name>`.

**אין שום שינוי ב-.NET או ב-Frontend.** הוולידציה, התקציר, האישור והמנוע נשארים כמו שהם.

---

## משימה מתקדמת (רשות): AI שמנתח את מבנה ה-DB ומציע בקרות - תכנון בלבד

> החלק הזה הוא **תכנון**. הוא לא ממומש.

### המטרה

במקום שהמשתמש יתאר בקרה, המערכת מציעה מיוזמתה רשימה של בקרות איכות נתונים רלוונטיות, עם הסבר ועם מספר החריגות
שכל בקרה הייתה מוצאת היום. המשתמש בוחר אילו לשמור.

### עקרונות

1. **אותו פורמט ואותה ולידציה.** כל הצעה היא DefinitionJson רגיל שעובר את ה-Binder. אין מסלול שני ליצירת SQL.
2. **המודל לא רואה רשומות.** הוא מקבל את הקטלוג העסקי ופרופיל סטטיסטי מצטבר בלבד: אחוז ערכים ריקים, מינימום ומקסימום,
   מספר ערכים שונים, התפלגות ערכי רשימה. אין ערכים בודדים ואין מידע אישי (שמות, טלפונים).
3. **הפרופיל מחושב עם אותן הגבלות** כמו הרצת בקרה: `controls_runner`, SELECT בלבד, timeout, ושאילתות שנבנות רק מה-Metadata.
4. **הוכחה לפני הצעה.** כל הצעה מורצת כ-preview, ומוצגת יחד עם מספר החריגות ודוגמאות.
   הצעה עם 0 חריגות מסומנת "מניעתית", והצעה שתופסת את רוב הרשומות נחשדת כשגויה.

### רכיבים

```
GET /api/ai/control-proposals?entity=Delivery
   │
   ▼
.NET ─ DataProfiler (חדש, Infrastructure)
   │   לכל שדה בקטלוג: COUNT, אחוז NULL/ריק, MIN/MAX, COUNT DISTINCT, התפלגות enum (TOP N)
   │   לכל קשר: אחוז רשומות "יתומות" (LEFT JOIN בלי התאמה)
   │   SQL נבנה מה-Metadata בלבד ומורץ תחת controls_runner, עם cache לפי גרסת ה-Metadata
   ▼
.NET ─ ProposalService (חדש, Application)
   │   שולח {catalog, profile, existingControls} ל-Python
   ▼  POST /propose-controls
Python ─ ProposalPromptBuilder + AiProvider
   │   מחזיר: [{definition, title, rationale, severity, category}]
   │   Mock/Heuristic provider: כללים דטרמיניסטיים (ראו בהמשך)
   ▼
.NET ─ לכל הצעה: Parse → Bind → Preview (TotalCount)
   │   סינון: לא תקינה, כפולה לבקרה קיימת (השוואת הגדרה קנונית), 0% או 100% חריגות
   ▼
UI ─ רשימת הצעות: כותרת, הסבר, מספר חריגות, דוגמאות ← בחירה ועריכה ← שמירה (source = AI)
```

### הצעות שגם ספק היוריסטי (בלי מודל) יכול להפיק

ספק כזה יכול לשמש כ-Mock לשלב הזה, ומודל אמיתי ירחיב אותו בהצעות סמנטיות.

| דפוס בקטלוג ובפרופיל | הצעה |
|---|---|
| שדה טקסט שכמעט תמיד מלא (למשל 95%) | "`<שדה>` ריק" (חוסר במידע שבדרך כלל קיים) |
| שדה מספרי שלא אמור להיות שלילי (כמות, משקל, מרחק) | "`<שדה>` קטן מ-0" |
| שני שדות תאריך באותה ישות עם שמות "התחלה/סיום" או "יציאה/מסירה" | "`<סיום>` לפני `<התחלה>`" |
| שדה תאריך של אירוע שכבר קרה | "`<שדה>` אחרי עכשיו" |
| שדה "פעיל" בישות קשורה | "רשומה פעילה שמשויכת ל-`<ישות קשורה>` לא פעילה" |
| שדה מספרי בישות + שדה "מרבי" באותה יחידה בישות קשורה | "`<שדה>` גדול מ-`<מרבי>` (`<קשר>`)" (בדומה לבקרות 1 ו-2) |
| ערך רשימה + תאריך שאמור להתאים לו | "סטטוס `<X>` ו-`<תאריך>` ריק" (בדומה לבקרה 5) |
| ערכי קיצון לפי הפרופיל (מעל אחוזון 99) | "`<שדה>` גדול מ-`<סף>`", בביטחון נמוך ועם בקשה לאישור הסף |

### מה המודל מוסיף מעבר להיוריסטיקה

הבנה סמנטית של שמות ותיאורים בעברית. למשל: "תוקף רישיון" צריך להיות אחרי "זמן יציאה", ו"מועד תחזוקה הבא" שעבר
רלוונטי רק לרחפן בשירות. המודל גם יכול לנסח הסבר עסקי ולקבוע חומרה.

### שלבי מימוש מוצעים

1. `DataProfiler` + endpoint שמחזיר פרופיל (שימושי גם בלי AI, כמסך "בריאות נתונים").
2. ספק היוריסטי ב-Python + `ProposalService` + preview אוטומטי.
3. מסך הצעות ב-Frontend.
4. ספק מודל אמיתי, עם אותו חוזה.
5. משוב: הצעות שנדחו נשמרות ולא מוצעות שוב.

### סיכונים

| סיכון | התמודדות |
|---|---|
| עומס על ה-DB בזמן חישוב הפרופיל | הרצה ידנית או מתוזמנת, cache, ודגימה (`TABLESAMPLE`) בטבלאות גדולות |
| דליפת מידע אישי למודל | רק אגרגציות. שדות שמסומנים במטא כרגישים לא נכנסים לפרופיל |
| הצעות רבות מדי ("רעש") | דירוג לפי חומרה ואחוז חריגות, הגבלה ל-N הצעות, ומנגנון "לא להציע שוב" |
| הצעה שגויה סמנטית | ההצעה מוצגת עם preview ודורשת אישור. שום דבר לא נשמר אוטומטית |
