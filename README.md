# רדאר - בקרות איכות נתונים

מערכת Web גנרית שמאפשרת למשתמשים עסקיים להגדיר בקרות איכות נתונים בלי SQL ובלי להכיר את מבנה הטבלאות,
להריץ אותן על SQL Server ולראות את הרשומות החריגות יחד עם הסיבה לכל חריגה.

- **גנריות:** שכבת Metadata (`meta.*`) ממפה מונחים עסקיים בעברית ("משלוח", "כושר נשיאה") לטבלאות, לעמודות ולקשרים.
  בקרה נשמרת כ-JSON של תנאים (ולא כ-SQL), ומתורגמת ל-SQL רק בזמן ריצה. ישות חדשה מתווספת דרך Metadata בלבד, בלי שינוי קוד
  (ראו `database/06_demo_add_weather_alerts.sql`).
- **אבטחה:** מזהים רק מתוך ה-whitelist של ה-Metadata, כל ערך הוא פרמטר, נוצר רק `SELECT`, וכל הרצה מתבצעת תחת
  `EXECUTE AS USER = 'controls_runner'` (הרשאת SELECT על `ops` בלבד), עם timeout ומגבלת שורות.
- **שלב ב' (AI):** תיאור בקרה בשפה חופשית מתורגם להצעת הגדרה במבנה זהה, דרך שירות Python נפרד (Mock).
  ההצעה מוצגת למשתמש לאישור לפני שמירה או הרצה.

תחום הנתונים לדוגמה: צי רחפני משלוחים (מוקדים, רחפנים, מפעילים, לקוחות, משלוחים).

| מסמך | תוכן |
|---|---|
| [docs/architecture.md](docs/architecture.md) | ארכיטקטורה ומנגנון הבקרות: זרימה, ולידציה, בניית SQL, הרצה, API, שכבת ה-AI |
| [docs/control-examples.md](docs/control-examples.md) | 14 הבקרות לדוגמה ו-3 בקרות "חיות" לדמו |
| [docs/stage-b-ai.md](docs/stage-b-ai.md) | הדגמת שלב ב' ותכנון המשימה המתקדמת |
| [docs/assumptions-limitations.md](docs/assumptions-limitations.md) | הנחות עבודה, מגבלות והרחבות אפשריות |
| [docs/ai-tools.md](docs/ai-tools.md) | כלי ה-AI ששימשו בפיתוח |
| [docs/db-instructions.md](docs/db-instructions.md) | מפרט בסיס הנתונים ומבנה ה-JSON של בקרה |
| [ai-service/README.md](ai-service/README.md) | שירות ה-AI: endpoints, חוזה הקטלוג, Mock, החלפת מודל |

## דרישות מוקדמות

| רכיב | גרסה | בדיקה |
|---|---|---|
| .NET SDK | 10.0 | `dotnet --version` |
| Node.js | 24 | `node --version` |
| Angular CLI | 22 | `ng version` (התקנה: `npm install -g @angular/cli`) |
| Python | 3.13 | `python --version` |
| SQL Server Express | עם אימות Windows, מופע `localhost\SQLEXPRESS` | `sqlcmd -S localhost\SQLEXPRESS -E -C -Q "SELECT @@VERSION"` |
| sqlcmd | כלי שורת הפקודה של SQL Server | `sqlcmd -?` |

> **הערה על תעודות:** SQL Server Express מקומי משתמש בתעודה self-signed.
> לכן בכל פקודת `sqlcmd` מוסיפים `-C` (trust server certificate), ומחרוזת החיבור של ה-API כוללת
> `TrustServerCertificate=True`. זה מתאים לסביבת פיתוח מקומית בלבד. בסביבת ייצור יש להתקין תעודה חתומה ולהסיר את שתי ההגדרות.

## התקנה והרצה

כל הפקודות ב-PowerShell, מתוך תיקיית השורש של הפרויקט. ה-API, שירות ה-AI וה-Frontend רצים כל אחד בחלון נפרד.

### 1. בסיס נתונים

יוצר את `DroneOpsControls` (סכמות `ops`, `meta`, `ctl`), נתוני דוגמה, Metadata, 14 בקרות לדוגמה ומשתמשי הרשאות.
הסקריפטים מוחקים ויוצרים מחדש, כך שאפשר להריץ אותם שוב.

```powershell
Get-ChildItem .\database\0[1-5]*.sql | Sort-Object Name | ForEach-Object {
    sqlcmd -S localhost\SQLEXPRESS -E -C -b -f 65001 -i $_.FullName
}

# אימות: 14 שאילתות שנכתבו ידנית מול התוצאות הצפויות. צפוי Passed = 1 בכל 14 השורות
sqlcmd -S localhost\SQLEXPRESS -E -C -f 65001 -i .\database\99_verify.sql
```

- `-f 65001` קורא את הסקריפטים כ-UTF-8, כדי שהעברית תישמר נכון.
- התאריכים בנתונים יחסיים ליום ההרצה, כך שבקרות כמו "פג תוקף" ו"באיחור" עובדות בכל יום.
- **אופציונלי, הדגמת גנריות:** `06_demo_add_weather_alerts.sql` מוסיף ישות חדשה ("התראת מזג אוויר") דרך Metadata בלבד.
  אפשר להריץ אותו שוב:
  ```powershell
  sqlcmd -S localhost\SQLEXPRESS -E -C -b -f 65001 -i .\database\06_demo_add_weather_alerts.sql
  ```
  אם ה-API כבר רץ, צריך לרענן את הקטלוג (או לחכות 5 דקות, משך ה-cache):
  `Invoke-RestMethod -Method Post http://localhost:5149/api/metadata/refresh`

### 2. API (.NET)

```powershell
cd backend
dotnet run --project DataControls.Api
```

- כתובת: http://localhost:5149. Swagger UI זמין ב-http://localhost:5149/swagger (בסביבת Development).
- ההגדרות נמצאות ב-`backend/DataControls.Api/appsettings.json`:
  - `ConnectionStrings:DroneOps`
  - `ControlExecution` (מגבלת שורות, timeout, משתמש ההרצה)
  - `Metadata`
  - `AiService:BaseUrl` (ברירת מחדל `http://localhost:8001`)
  - `Cors:AllowedOrigins` (ברירת מחדל `http://localhost:4200`)
- `backend/DataControls.Api/DataControls.Api.http` כולל בקשות לדוגמה.

### 3. שירות ה-AI (Python)

השירות אופציונלי. בלעדיו כל המערכת עובדת, ורק `/api/ai/*` מחזיר 503 עם הודעה בעברית.

```powershell
cd ai-service
python -m venv .venv                      # פעם ראשונה בלבד
.venv\Scripts\activate
pip install -r requirements-dev.txt       # פעם ראשונה בלבד
uvicorn main:app --reload --port 8001
```

- Swagger: http://localhost:8001/docs. בדיקת חיים: http://localhost:8001/health.
- ספק ה-AI נבחר במשתנה הסביבה `AI_PROVIDER` (ברירת מחדל `mock`).
- אם `pip` נכשל בשגיאת תעודה (רשת עם סינון TLS), מגדירים את קובץ ה-CA של הארגון:
  `pip config set global.cert <path-to-ca.crt>`. אין לבטל אימות TLS.

### 4. Frontend (Angular)

```powershell
cd frontend
npm install        # פעם ראשונה בלבד
npm start
```

- כתובת: http://localhost:4200. ה-UI פונה ל-API ב-http://localhost:5149.

### סדר מומלץ לדמו

1. בסיס נתונים (01–05) ואימות (99).
2. API, ואז שירות ה-AI ו-Frontend.
3. במסך הבקרות: הרצת אחת מ-14 הבקרות לדוגמה, וצפייה בחריגות ובסיבות.
4. הגדרת בקרה חדשה דרך המסך (ראו [docs/control-examples.md](docs/control-examples.md)).
5. שלב ב': תיאור בקרה בשפה חופשית, בדיקת ההצעה ואישורה (ראו [docs/stage-b-ai.md](docs/stage-b-ai.md)).
6. גנריות: הרצת `06` ורענון הקטלוג. הישות החדשה מופיעה ואפשר להגדיר עליה בקרה.

## בדיקות

```powershell
# Backend: unit + API + integration
cd backend
dotnet test

# בלי בסיס נתונים (בדיקות האינטגרציה ידלגו גם אוטומטית אם ה-DB לא זמין)
dotnet test --filter "Category!=Integration"

# שירות ה-AI
cd ai-service
.venv\Scripts\python -m pytest
```

- **Backend (xUnit):** Serializer, Binder, QueryBuilder (כל אופרטור, joins, קינון, ניסיונות injection), סיבות חריגה,
  API (על קטלוג בזיכרון), שכבת ה-AI (generator מזויף ו-`HttpMessageHandler` מזויף), ואינטגרציה מול ה-DB.
  התוצאות הצפויות של בדיקות האינטגרציה נקראות ישירות מ-`database/99_verify.sql`, ולכן הוא מקור האמת היחיד.
- **AI service (pytest):** ולידציה מול הקטלוג, זיהוי בקשות ב-Mock, PromptBuilder, ה-API.
  כולל בדיקה שכל 14 ההגדרות מ-`04_sample_controls.sql` תקינות.
- connection string אחר לבדיקות האינטגרציה: משתנה הסביבה `DATACONTROLS_TEST_DB`.

## מבנה התיקיות

```
/database                  סקריפטי SQL: 01_schema, 02_seed_data, 03_metadata, 04_sample_controls,
                           05_security, 06_demo_add_weather_alerts (אופציונלי), 99_verify
/backend                   DataControls.sln (.NET 10)
  DataControls.Domain          מודל ההגדרה (group / rule / operand) + Serializer, מודל ה-Metadata
  DataControls.Application     DefinitionBinder, שירותים (בקרות, הרצות, קטלוג עסקי, הצעות AI), ממשקים
  DataControls.Infrastructure  גישה ל-SQL Server (Dapper), SqlQueryBuilder, SqlControlRunner, לקוח HTTP לשירות ה-AI
  DataControls.Api             Controllers, DTOs, ProblemDetails בעברית, OpenAPI
  DataControls.Tests           Unit / Api / Integration
/ai-service                Python FastAPI: /generate-control, /prompt-preview, providers/ (AiProvider + MockProvider)
/frontend                  Angular (standalone components, עברית, RTL)
/docs                      ארכיטקטורה, דוגמאות בקרות, שלב ב', הנחות ומגבלות, כלי AI, מפרט ה-DB
  requests/                בקשות לדוגמה לשלב ב' (UTF-8)
```
