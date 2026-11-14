# רדאר - בקרות איכות נתונים

## התחלה מהירה

מתוך תיקיית הפרויקט, ב-PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\start.ps1
```

הסקריפט בודק את הדרישות המוקדמות ([טבלה](#דרישות-מוקדמות)) ומסביר מה חסר, מוצא את SQL Server
(`localhost\SQLEXPRESS`, מופע מקומי אחר או LocalDB), יוצר את בסיס הנתונים אם הוא לא קיים, מתקין תלויות, מפעיל את
שלושת השירותים, כל אחד בחלון ממוזער משלו, ופותח את הדפדפן ב-http://localhost:4200.
הרצה ראשונה לוקחת כמה דקות (התקנת חבילות), והרצות הבאות פחות מחצי דקה.

```powershell
powershell -ExecutionPolicy Bypass -File .\stop.ps1                                     # עצירת כל השירותים
powershell -ExecutionPolicy Bypass -File .\start.ps1 -SqlInstance "(localdb)\MSSQLLocalDB"  # מופע SQL Server מסוים
powershell -ExecutionPolicy Bypass -File .\start.ps1 -ResetDatabase                     # בניית בסיס הנתונים מחדש
powershell -ExecutionPolicy Bypass -File .\start.ps1 -NoBrowser                         # בלי פתיחת דפדפן
```

- **בסיס הנתונים נשמר בין הרצות:** `start.ps1` יוצר אותו רק אם הוא לא קיים, ולכן בקרות והרצות שיצרתם נשארות.
  `-ResetDatabase` מוחק ובונה מחדש.
- `-ExecutionPolicy Bypass` נדרש כי ברירת המחדל של Windows חוסמת סקריפטים, וגם סקריפטים שחולצו מ-ZIP שהורד מהאינטרנט.
- הרצה של `start.ps1` כשהמערכת כבר רצה מפעילה אותה מחדש.
- שירות ה-AI אופציונלי. אם הוא לא עולה או לא מוגדר, הסקריפט מציג אזהרה, וכל שאר המערכת עובדת
  (ראו [שירות ה-AI לא מוגדר](#שירות-ה-ai-לא-מוגדר-או-חסר-מפתח-api)).
- ההתקנה הידנית, צעד אחר צעד, מתוארת ב[התקנה והרצה](#התקנה-והרצה).

## צילומי מסך

<!--
  להוספת צילום: שומרים את הקובץ ב-docs/screenshots/ בשם שבטבלה, ומוחקים את סימני ההערה מסביב לשורה שלו למטה.
  רוחב מומלץ: 1400 פיקסלים. פירוט: docs/screenshots/README.md
-->

| מסך | קובץ |
|---|---|
| רשימת הבקרות | `docs/screenshots/01-controls-list.png` |
| עורך בקרה: בונה תנאים מקונן ותקציר בעברית | `docs/screenshots/02-control-editor.png` |
| תוצאות הרצה: חריגות וסיבה לכל רשומה | `docs/screenshots/03-run-results.png` |
| יצירת בקרה בעזרת AI | `docs/screenshots/04-create-with-ai.png` |

<!-- ![רשימת הבקרות](docs/screenshots/01-controls-list.png) -->
<!-- ![עורך בקרה](docs/screenshots/02-control-editor.png) -->
<!-- ![תוצאות הרצה](docs/screenshots/03-run-results.png) -->
<!-- ![יצירת בקרה בעזרת AI](docs/screenshots/04-create-with-ai.png) -->

## על המערכת

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
| [docs/submission.md](docs/submission.md) | מסמך ההגשה התמציתי (8 עמודים). גרסה להדפסה: `docs/submission.html`, נבנית ב-`python tools\md_to_html.py` |
| [docs/architecture.md](docs/architecture.md) | ארכיטקטורה ומנגנון הבקרות: זרימה, ולידציה, בניית SQL, הרצה, API, שכבת ה-AI |
| [docs/control-examples.md](docs/control-examples.md) | 14 הבקרות לדוגמה ו-3 בקרות "חיות" לדמו |
| [docs/stage-b-ai.md](docs/stage-b-ai.md) | הדגמת שלב ב' ותכנון המשימה המתקדמת |
| [docs/assumptions-limitations.md](docs/assumptions-limitations.md) | הנחות עבודה, מגבלות והרחבות אפשריות |
| [docs/ai-tools.md](docs/ai-tools.md) | כלי ה-AI ששימשו בפיתוח |
| [docs/db-instructions.md](docs/db-instructions.md) | מפרט בסיס הנתונים ומבנה ה-JSON של בקרה |
| [docs/requirements.md](docs/requirements.md) | דרישות המטלה (המקור: [docs/requirements.docx](docs/requirements.docx)) |
| [ai-service/README.md](ai-service/README.md) | שירות ה-AI: endpoints, חוזה הקטלוג, Mock, החלפת מודל |
| [frontend/README.md](frontend/README.md) | ה-Frontend: הרצה, בנייה ומבנה הקוד (core / shared / features) |

## דרישות מוקדמות

| רכיב | גרסה | בדיקה |
|---|---|---|
| .NET SDK | 10.0 | `dotnet --version` |
| Node.js | 24 | `node --version` |
| Python | 3.13 | `python --version` |
| SQL Server Express | עם אימות Windows, מופע `localhost\SQLEXPRESS` | `sqlcmd -S localhost\SQLEXPRESS -E -C -Q "SELECT @@VERSION"` |
| sqlcmd | כלי שורת הפקודה של SQL Server | `sqlcmd -?` |

> **הערה על תעודות:** SQL Server Express מקומי משתמש בתעודה self-signed.
> לכן בכל פקודת `sqlcmd` מוסיפים `-C` (trust server certificate), ומחרוזת החיבור של ה-API כוללת
> `TrustServerCertificate=True`. זה מתאים לסביבת פיתוח מקומית בלבד. בסביבת ייצור יש להתקין תעודה חתומה ולהסיר את שתי ההגדרות.

אין צורך בהתקנה גלובלית של Angular CLI: הוא מותקן עם `npm install` בתיקיית `frontend`, וכל הפקודות משתמשות בו דרך
`npm start` או `npx ng`.

## התקנה והרצה

כל הפקודות ב-PowerShell, מתוך תיקיית השורש של הפרויקט. ה-API, שירות ה-AI וה-Frontend רצים כל אחד בחלון נפרד.

### 1. בסיס נתונים

יוצר את `DroneOpsControls` (סכמות `ops`, `meta`, `ctl`), נתוני דוגמה, Metadata, 14 בקרות לדוגמה ומשתמשי הרשאות.
הסקריפטים מוחקים ויוצרים מחדש, כך שאפשר להריץ אותם שוב.

> **אזהרה:** `01_schema.sql` מנתק את כל החיבורים הפעילים ל-`DroneOpsControls`
> (`SET SINGLE_USER WITH ROLLBACK IMMEDIATE`) ומוחק את בסיס הנתונים. בקרות שנוצרו, הרצות ותוצאות קודמות נמחקות,
> ונשארות רק 14 בקרות הדוגמה. אם ה-API רץ, החיבורים שלו מתנתקים, ועדיף לעצור אותו לפני ההרצה ולהפעיל אותו מחדש אחריה.

```powershell
Get-ChildItem .\database\0[1-5]*.sql | Sort-Object Name | ForEach-Object {
    sqlcmd -S localhost\SQLEXPRESS -E -C -b -f 65001 -i $_.FullName
}

# אימות: 14 שאילתות שנכתבו ידנית מול התוצאות הצפויות, ובדיקות הרשאה
sqlcmd -S localhost\SQLEXPRESS -E -C -f 65001 -W -s "|" -i .\database\99_verify.sql
```

- **תוצאה צפויה:** 14 שורות (אחת לכל בקרה) עם `Passed` = 1, שורת סיכום `14|14` (`PassedControls|TotalControls`),
  ו-3 בדיקות הרשאה של `controls_runner` עם `Passed` = 1: SELECT על `ops` מותר, ו-UPDATE על `ops` ו-SELECT על `ctl` נחסמים.
  הודעות "permission was denied" בבדיקות האלה הן התוצאה הנכונה.
- `-W -s "|"` מצמצם את רוחב העמודות. בלעדיהם הפלט תקין, אבל רחב מאוד וקשה לקריאה.
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

- Swagger: http://localhost:8001/docs. בדיקת חיים: http://localhost:8001/health (צפוי `{"status":"ok","provider":"mock"}`).
- אם `.venv\Scripts\activate` נחסם בשגיאה "running scripts is disabled on this system" (ברירת המחדל של Windows PowerShell),
  אפשר לאשר סקריפטים מקומיים למשתמש הנוכחי: `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`,
  או לדלג על ההפעלה ולהריץ ישירות דרך ה-venv: `.venv\Scripts\python -m uvicorn main:app --reload --port 8001`.
- ספק ה-AI נבחר במשתנה הסביבה `AI_PROVIDER` (ברירת מחדל `mock`).
- אם `pip` נכשל בשגיאת תעודה (רשת עם סינון TLS), מגדירים את קובץ ה-CA של הארגון:
  `pip config set global.cert <path-to-ca.crt>`. אין לבטל אימות TLS.

### 4. Frontend (Angular)

```powershell
cd frontend
npm install        # פעם ראשונה בלבד
npm start
```

- כתובת: http://localhost:4200. הקריאות ל-`/api` מועברות ל-API (http://localhost:5149) דרך `frontend/proxy.conf.json`,
  כך שאין תלות ב-CORS. ה-API צריך לרוץ (שלב 2) כדי שהמסכים יציגו נתונים.
- אם `npm install` נכשל בשגיאת תעודה (רשת עם סינון TLS): `npm config set cafile <path-to-ca.crt>`. אין לבטל אימות TLS.
- ב-npm 11 מופיעה בסוף `npm install` האזהרה `4 packages have install scripts not yet covered by allowScripts`, עבור
  `esbuild`, `lmdb`, `@parcel/watcher` ו-`msgpackr-extract`. היא צפויה ולא מזיקה: החבילות האלה מגיעות עם קבצים בינאריים
  מוכנים לפלטפורמה, וה-build, שרת הפיתוח והבדיקות עובדים בלי להריץ את הסקריפטים. אין צורך לאשר אותם.

### בדיקת תקינות אחרי ההפעלה

הכתובות לפי הפורטים שבברירת המחדל. אם שיניתם פורט, ראו [פורטים](#פורטים).

```powershell
Invoke-RestMethod http://localhost:8001/health                # שירות ה-AI: status = ok
Invoke-RestMethod http://localhost:5149/api/metadata/entities # ה-API: 5 ישויות (6 אחרי סקריפט 06)
Invoke-RestMethod http://localhost:4200/api/metadata/entities # אותו דבר, דרך ה-proxy של ה-Frontend
```

### פורטים

| שירות | פורט | איפה משנים | מה לעדכן בנוסף |
|---|---|---|---|
| Backend (API) | 5149 | `backend/DataControls.Api/Properties/launchSettings.json` (`applicationUrl`; בפרופיל `https` גם 7217) | `target` ב-`frontend/proxy.conf.json` |
| Frontend | 4200 | חד-פעמי: `npm start -- --port <port>` (מעביר `--port` ל-`ng serve`). קבוע: ראו מתחת לטבלה | `Cors:AllowedOrigins` ב-`backend/DataControls.Api/appsettings.json` |
| שירות ה-AI | 8001 | `--port` בפקודת `uvicorn` | `AiService:BaseUrl` ב-`backend/DataControls.Api/appsettings.json` |

- **פורט קבוע ל-Frontend:** ב-`frontend/angular.json` אין כרגע מפתח `port`. מוסיפים אותו לאובייקט
  `projects.frontend.architect.serve.options`, ליד `proxyConfig` שכבר נמצא שם:
  ```json
  "options": {
    "proxyConfig": "proxy.conf.json",
    "port": 4300
  }
  ```
- ה-Frontend פונה ל-API רק דרך ה-proxy, ולכן `Cors:AllowedOrigins` נדרש רק למי שקורא ל-API ישירות מהדפדפן בכתובת אחרת.
  כדאי לעדכן אותו בכל מקרה כשמשנים את פורט ה-Frontend.
- אחרי שינוי ב-`appsettings.json` או ב-`launchSettings.json` צריך להפעיל מחדש את ה-API. אחרי שינוי ב-`proxy.conf.json`
  צריך להפעיל מחדש את `npm start`.

### סדר מומלץ לדמו

1. בסיס נתונים (01–05) ואימות (99).
2. API, ואז שירות ה-AI ו-Frontend.
3. במסך הבקרות: הרצת אחת מ-14 הבקרות לדוגמה, וצפייה בחריגות ובסיבות.
4. הגדרת בקרה חדשה דרך המסך (ראו [docs/control-examples.md](docs/control-examples.md)).
5. שלב ב': תיאור בקרה בשפה חופשית, בדיקת ההצעה ואישורה (ראו [docs/stage-b-ai.md](docs/stage-b-ai.md)).
6. גנריות: הרצת `06` ורענון הקטלוג. הישות החדשה מופיעה ואפשר להגדיר עליה בקרה.

### בקשות לדוגמה לשלב ב' (`docs/requests/`)

כל קובץ הוא גוף בקשה מוכן ל-`POST /api/ai/suggest-control`, בפורמט `{"text": "..."}`, שמור כ-UTF-8.
1–5 הן הבקשות שה-Mock מזהה, ו-`6-unrecognized.json` מדגים בקשה שלא זוהתה (`definition: null` עם הסבר).
שליחה מ-PowerShell, מתוך תיקיית השורש, כשה-API ושירות ה-AI רצים:

```powershell
$body = [IO.File]::ReadAllBytes("docs\requests\2-active-drone-closed-hub.json")   # הבתים כפי שהם, בלי המרת קידוד
$r = Invoke-RestMethod -Method Post http://localhost:5149/api/ai/suggest-control `
       -ContentType "application/json; charset=utf-8" -Body $body
$r.rootEntity, $r.isValid, $r.confidence       # Drone, True, 0.9
$r.definition | ConvertTo-Json -Depth 10       # ההגדרה המוצעת
```

את אותם קבצים אפשר להעלות גם ב-Swagger (`/swagger`), או להדביק את הטקסט שבהם במסך "צור בעזרת AI".
אם העברית מוצגת בקונסול כסימנים משובשים, זו רק בעיית תצוגה של החלון: `[Console]::OutputEncoding = [Text.Encoding]::UTF8`.

## בדיקות

מתוך תיקיית השורש. כל בלוק עובר לתיקייה שלו וחוזר לשורש בסוף, כך שאפשר להריץ אותם ברצף.

```powershell
# Backend: unit + API + integration
# אם ה-API רץ והקוד שלו השתנה מאז שעלה, ה-build לא יכול להחליף את הקבצים הנעולים (MSB3027). עוצרים קודם: .\stop.ps1
cd backend
dotnet test
# בלי בסיס נתונים (בדיקות האינטגרציה ידלגו גם אוטומטית אם ה-DB לא זמין)
dotnet test --filter "Category!=Integration"
cd ..

# שירות ה-AI (אחרי יצירת ה-venv בשלב 3)
cd ai-service
.venv\Scripts\python -m pytest
cd ..

# Frontend: בנייה (צפויה בלי שגיאות ובלי אזהרות) ובדיקות יחידה (Vitest), אחרי npm install בשלב 4
cd frontend
npx ng build
npx ng test --watch=false
cd ..
```

- **Backend (xUnit):** Serializer, Binder, QueryBuilder (כל אופרטור, joins, קינון, ניסיונות injection), סיבות חריגה,
  API (על קטלוג בזיכרון), שכבת ה-AI (generator מזויף ו-`HttpMessageHandler` מזויף), ואינטגרציה מול ה-DB.
  התוצאות הצפויות של בדיקות האינטגרציה נקראות ישירות מ-`database/99_verify.sql`, ולכן הוא מקור האמת היחיד.
- **AI service (pytest):** ולידציה מול הקטלוג, זיהוי בקשות ב-Mock, PromptBuilder, ה-API.
  כולל בדיקה שכל 14 ההגדרות מ-`04_sample_controls.sql` תקינות.
- **Frontend (Vitest):** עץ ההגדרה והמרות, נתיבי שגיאות, התקציר בעברית, פענוח ProblemDetails, מסך ה-AI (הצעה, פתיחה בעורך
  בלי שמירה, שירות לא זמין) ותצוגה מקדימה בעורך.
- connection string אחר לבדיקות האינטגרציה: משתנה הסביבה `DATACONTROLS_TEST_DB`.

## פתרון בעיות

### שירות ה-AI לא מוגדר או חסר מפתח API

ספק ה-Mock (ברירת המחדל) לא צריך מפתח API. אם משתנה הסביבה `AI_PROVIDER` מצביע על ספק שלא קיים, או על ספק שחסר לו
מפתח, שירות ה-AI **לא קורס**. הוא עולה במצב "לא מוגדר":

- `GET http://localhost:8001/health` מחזיר 503 עם `{"status":"not_configured","provider":...,"detail":...}`, ו-`start.ps1`
  מציג אזהרה.
- במסך "צור בעזרת AI" מופיעה הודעה בעברית עם הסיבה, למשל שהספק אינו מוכר או איזה משתנה סביבה חסר, וקישור להגדרה ידנית.
- כל שאר המערכת (בקרות, הרצות, תוצאות) ממשיכה לעבוד כרגיל.

התיקון: מגדירים את המשתנה החסר, או מוחקים את `AI_PROVIDER` כדי לחזור ל-Mock, ומפעילים מחדש
(`.\stop.ps1` ואז `.\start.ps1`).

### פורט תפוס

קודם כל: `powershell -ExecutionPolicy Bypass -File .\stop.ps1` עוצר כל תהליך של הפרויקט על 5149, 8001 ו-4200, ומדווח
אם הפורט תפוס על ידי תוכנה אחרת (בלי לגעת בה). `start.ps1` עושה את זה בעצמו לפני ההפעלה.

הודעה כמו `address already in use`, `Failed to bind to address` (API), `[Errno 10048]` (uvicorn), או `Port 4200 is already in use`
(Angular) פירושה שתהליך אחר כבר מאזין לפורט. בדרך כלל זה מופע קודם של אותו שירות שלא נסגר.

1. **מי מאזין לפורט** (5149, 4200 או 8001):
   ```powershell
   netstat -ano | findstr ":8001"            # העמודה האחרונה בשורת LISTENING היא מזהה התהליך (PID)
   Get-NetTCPConnection -LocalPort 8001 -State Listen | Select-Object OwningProcess   # חלופה
   Get-Process -Id <PID>                     # מה התהליך
   ```
2. **אם זה מופע ישן של השירות:** סוגרים את החלון שלו, או עוצרים אותו יחד עם כל תהליכי המשנה שלו:
   ```powershell
   taskkill /PID <PID> /T /F
   ```
3. **או שמריצים על פורט אחר**, לפי [טבלת הפורטים](#פורטים), כולל עדכון הקבצים שתלויים בו.

### תהליך uvicorn יתום על 8001

עם `--reload`, uvicorn מריץ שלושה תהליכי Python: תהליך ראשי, תהליך שמאזין לפורט, ו-worker שמתחיל ב-`spawn_main`.
אם החלון נסגר בצורה לא מסודרת, אחד מהם יכול להישאר ולהחזיק את 8001. כדי למצוא את כל התהליכים של השירות:

```powershell
Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
  Where-Object { $_.CommandLine -match 'uvicorn|multiprocessing\.spawn' } |
  Select-Object ProcessId, ParentProcessId, CommandLine
```

בודקים ב-`CommandLine` שהתהליכים שייכים ל-`ai-service` (הנתיב של ה-venv מופיע בו; ה-worker מופיע עם `spawn_main`
והמזהה של תהליך האב שלו), ואז עוצרים אותם:

```powershell
Stop-Process -Id <PID1>,<PID2>,<PID3> -Force
```

ומוודאים שהפורט התפנה: `netstat -ano | findstr ":8001"` לא מחזיר שורת `LISTENING`.

## מבנה התיקיות

```
/database                  סקריפטי SQL: 01_schema, 02_seed_data, 03_metadata, 04_sample_controls,
                           05_security, 06_demo_add_weather_alerts (אופציונלי), 99_verify
/backend                   DataControls.slnx (.NET 10, פורמט ה-solution החדש)
  DataControls.Domain          מודל ההגדרה (group / rule / operand) + Serializer, מודל ה-Metadata
  DataControls.Application     DefinitionBinder, שירותים (בקרות, הרצות, קטלוג עסקי, הצעות AI), ממשקים
  DataControls.Infrastructure  גישה ל-SQL Server (Dapper), SqlQueryBuilder, SqlControlRunner, לקוח HTTP לשירות ה-AI
  DataControls.Api             Controllers, DTOs, ProblemDetails בעברית, OpenAPI
  DataControls.Tests           Unit / Api / Integration
/ai-service                Python FastAPI: /generate-control, /prompt-preview, providers/ (AiProvider + MockProvider)
/frontend                  Angular (standalone components, עברית, RTL)
/docs                      מסמך ההגשה, ארכיטקטורה, דוגמאות בקרות, שלב ב', הנחות ומגבלות, כלי AI, מפרט ה-DB, דרישות המטלה
  requests/                בקשות לדוגמה לשלב ב' (UTF-8), ראו "בקשות לדוגמה לשלב ב'"
/tools                     md_to_html.py: ממיר את docs/submission.md ל-docs/submission.html (ספרייה סטנדרטית בלבד)
start.ps1 / stop.ps1       הפעלה ועצירה בפקודה אחת (ראו "התחלה מהירה")
```
