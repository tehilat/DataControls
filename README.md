# 📡 רדאר - בקרות איכות נתונים

**רדאר** היא מערכת גנרית שמאפשרת למשתמשים עסקיים להגדיר בקרות איכות נתונים — בלי SQL ובלי שמות טבלאות —
להריץ אותן מול SQL Server ולראות את הרשומות החריגות עם סיבה בעברית לכל רשומה.
התחום לדוגמה: צי רחפני משלוחים (מוקדים, רחפנים, מפעילים, לקוחות, משלוחים).

## רכיבים

| תיקייה | תפקיד |
|---|---|
| `database/` | סקריפטים: סכמה, נתונים, קטלוג Metadata, בקרות לדוגמה, אבטחה, בדיקות |
| `backend/` | ‏.NET 10 Web API — מנוע הבקרות (Metadata → QueryBuilder → Runner), CRUD, הרצות ותוצאות |
| `frontend/` | ‏Angular 22 + Angular Material — ממשק בעברית (RTL): רשימת בקרות, עורך תנאים, תוצאות והיסטוריה |
| `ai-service/` | ‏Python FastAPI — טקסט חופשי → הגדרת בקרה (שלב ב', ספק Mock) |
| `docs/` | ארכיטקטורה, הנחות ומגבלות |

## הרצה מקומית

```powershell
# 1. מסד נתונים (פעם אחת)
Get-ChildItem .\database\0[1-5]*.sql | Sort-Object Name | ForEach-Object { sqlcmd -S localhost\SQLEXPRESS -E -C -b -f 65001 -i $_.FullName }

# 2. API  → http://localhost:5149  (Swagger: /swagger)
cd backend; dotnet run --project DataControls.Api

# 3. ממשק → http://localhost:4200  (הקריאות ל-/api עוברות דרך proxy.conf.json)
cd frontend; npm install; npm start
```

בדיקות: `cd backend; dotnet test` · בניית הממשק: `cd frontend; npx ng build`.
פרטים נוספים: `CLAUDE.md` ו-`docs/architecture.md`.
