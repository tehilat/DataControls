# דוגמאות לבקרות

כל בקרה נשמרת כ-JSON (`ctl.Controls.DefinitionJson`) ולא כ-SQL. רשומה נחשבת **חריגה** כשהתנאי מתקיים.
המבנה המלא מתועד ב-[db-instructions.md](db-instructions.md), ובקצרה:

```
{ "version": 1, "rootEntity": "<ישות>", "condition": <node> }
node    = { "type": "group", "logic": "AND" | "OR", "children": [...] }
        | { "type": "rule", "field": "<נתיב>", "operator": "<אופרטור>", "compareTo": <operand> }
operand = { "kind": "value", "value": ... } | { "kind": "field", "field": "<נתיב>" }
        | { "kind": "today", "offsetDays": <int> } | { "kind": "now" }
```

נתיב שדה הוא שדה של ישות הבסיס (`PackageWeightKg`) או שדה של ישות קשורה (`Drone.MaxPayloadKg`, `Drone.Hub.IsActive`).
המשתמש לא רואה את ה-JSON. הוא בוחר שדות ואופרטורים במונחים עסקיים, וה-JSON נבנה מאחורי הקלעים.

## 14 הבקרות לדוגמה

הבקרות נוצרות ב-`database/04_sample_controls.sql`. התוצאות הצפויות נבדקות בשתי דרכים:
`database/99_verify.sql` מריץ שאילתות שנכתבו ידנית, ובדיקות האינטגרציה מריצות את הבקרות דרך המנוע ומשוות לאותן תוצאות.
כל רשומה שגויה בנתונים מבודדת, כלומר נתפסת רק בבקרה שלה, אלא אם צוין אחרת.

| # | שם | ישות | חומרה | חריגות צפויות |
|---|---|---|---|---|
| 1 | משקל חבילה חורג מכושר הנשיאה | משלוח | גבוהה | משלוח 10 |
| 2 | מרחק משלוח חורג מטווח הטיסה | משלוח | גבוהה | משלוח 11 |
| 3 | זמן מסירה לפני זמן היציאה | משלוח | בינונית | משלוח 12 |
| 4 | מפעיל ללא רישיון בתוקף | משלוח | גבוהה | משלוחים 13, 14 |
| 5 | משלוח שנמסר ללא זמן מסירה | משלוח | בינונית | משלוח 15 |
| 6 | רחפן פעיל במוקד סגור | רחפן | בינונית | רחפן 9 |
| 7 | רחפן בשירות עם תחזוקה באיחור | רחפן | גבוהה | רחפנים 4, 5 |
| 8 | לקוח ללא פרטי קשר | לקוח | נמוכה | לקוח 6 |
| 9 | משלוח כבד וארוך ברחפן עם תחזוקה באיחור | משלוח | גבוהה | משלוח 9 |
| 10 | סטטוס משלוח לא עקבי | משלוח | בינונית | משלוחים 15, 16 |
| 11 | זמן יציאה עתידי | משלוח | נמוכה | משלוח 17 |
| 12 | משקל חבילה שלילי | משלוח | בינונית | משלוח 18 |
| 13 | משלוח ברחפן שנמצא בתחזוקה | משלוח | גבוהה | משלוח 19 |
| 14 | משלוח ע"י מפעיל לא פעיל | משלוח | בינונית | משלוח 20 |

### 1. משקל חבילה חורג מכושר הנשיאה

**תיאור עסקי:** משלוחים שבהם משקל החבילה גבוה מכושר הנשיאה של הרחפן שביצע אותם. מדגים השוואה בין שני שדות, אחד מהם דרך קשר.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "rule", "field": "PackageWeightKg", "operator": "gt",
    "compareTo": { "kind": "field", "field": "Drone.MaxPayloadKg" } } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 10.
הסיבה שמוצגת: `משקל חבילה (4.5 ק"ג) גדול מ-כושר נשיאה (רחפן) (3 ק"ג)`.

### 2. מרחק משלוח חורג מטווח הטיסה

**תיאור עסקי:** משלוחים שבהם מרחק הטיסה גדול מהטווח המרבי של הרחפן.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "rule", "field": "DistanceKm", "operator": "gt",
    "compareTo": { "kind": "field", "field": "Drone.MaxRangeKm" } } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 11.

### 3. זמן מסירה לפני זמן היציאה

**תיאור עסקי:** משלוחים שבהם זמן המסירה מוקדם מזמן היציאה מהמוקד. זו שגיאת נתונים, כי חבילה לא יכולה להגיע לפני שיצאה.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "rule", "field": "DeliveredAt", "operator": "lt",
    "compareTo": { "kind": "field", "field": "DispatchedAt" } } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 12.

### 4. מפעיל ללא רישיון בתוקף

**תיאור עסקי:** משלוחים שבוצעו על ידי מפעיל בלי רישיון בכלל, או שהרישיון שלו פג לפני זמן היציאה. מדגים קבוצת OR
והשוואה בין תאריך (תוקף רישיון) לתאריך ושעה (זמן יציאה).

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "group", "logic": "OR", "children": [
    { "type": "rule", "field": "Pilot.LicenseNumber", "operator": "isEmpty" },
    { "type": "rule", "field": "Pilot.LicenseExpiry", "operator": "lt",
      "compareTo": { "kind": "field", "field": "DispatchedAt" } } ] } }
```

**תוצאה צפויה:** 2 חריגות, משלוחים 13 ו-14 (אחד לכל אחד מהמקרים).
בקבוצת OR הסיבה מציגה רק את התנאי שהתקיים.

### 5. משלוח שנמסר ללא זמן מסירה

**תיאור עסקי:** משלוחים בסטטוס "נמסר" שאין להם זמן מסירה.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "group", "logic": "AND", "children": [
    { "type": "rule", "field": "Status", "operator": "eq", "compareTo": { "kind": "value", "value": "Delivered" } },
    { "type": "rule", "field": "DeliveredAt", "operator": "isEmpty" } ] } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 15. אותה רשומה נתפסת גם בבקרה 10, בכוונה.

### 6. רחפן פעיל במוקד סגור

**תיאור עסקי:** רחפנים בסטטוס "בשירות" שמשויכים למוקד שאינו פעיל.

```json
{ "version": 1, "rootEntity": "Drone", "condition":
  { "type": "group", "logic": "AND", "children": [
    { "type": "rule", "field": "Status", "operator": "eq", "compareTo": { "kind": "value", "value": "Active" } },
    { "type": "rule", "field": "Hub.IsActive", "operator": "eq", "compareTo": { "kind": "value", "value": false } } ] } }
```

**תוצאה צפויה:** 1 חריגה, רחפן 9.

### 7. רחפן בשירות עם תחזוקה באיחור

**תיאור עסקי:** רחפנים בשירות שמועד התחזוקה הבא שלהם כבר עבר. מדגים תאריך יחסי ("היום").

```json
{ "version": 1, "rootEntity": "Drone", "condition":
  { "type": "group", "logic": "AND", "children": [
    { "type": "rule", "field": "Status", "operator": "eq", "compareTo": { "kind": "value", "value": "Active" } },
    { "type": "rule", "field": "NextMaintenanceDue", "operator": "lt", "compareTo": { "kind": "today", "offsetDays": 0 } } ] } }
```

**תוצאה צפויה:** 2 חריגות, רחפנים 4 ו-5.
**מקרה גבול:** רחפן 7 עבר את מועד התחזוקה, אבל הוצא משירות, ולכן לא נתפס.

### 8. לקוח ללא פרטי קשר

**תיאור עסקי:** לקוחות שאין להם טלפון וגם אין להם אימייל. ערך של רווחים בלבד נחשב ריק.

```json
{ "version": 1, "rootEntity": "Customer", "condition":
  { "type": "group", "logic": "AND", "children": [
    { "type": "rule", "field": "Phone", "operator": "isEmpty" },
    { "type": "rule", "field": "Email", "operator": "isEmpty" } ] } }
```

**תוצאה צפויה:** 1 חריגה, לקוח 6.
**מקרה גבול:** ללקוח 7 יש רק אימייל, ולכן הוא לא נתפס.

### 9. משלוח כבד וארוך ברחפן עם תחזוקה באיחור

**תיאור עסקי:** משלוחים מעל 5 ק"ג ומעל 20 ק"מ שבוצעו ברחפן שמועד התחזוקה שלו עבר. מדגים שלושה תנאים, אחד מהם דרך קשר.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "group", "logic": "AND", "children": [
    { "type": "rule", "field": "PackageWeightKg", "operator": "gt", "compareTo": { "kind": "value", "value": 5 } },
    { "type": "rule", "field": "DistanceKm", "operator": "gt", "compareTo": { "kind": "value", "value": 20 } },
    { "type": "rule", "field": "Drone.NextMaintenanceDue", "operator": "lt", "compareTo": { "kind": "today", "offsetDays": 0 } } ] } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 9.
**מקרה גבול:** משלוח 5 בוצע ברחפן עם תחזוקה באיחור, אבל הוא קל וקצר, ולכן לא נתפס.

### 10. סטטוס משלוח לא עקבי

**תיאור עסקי:** משלוח בסטטוס "נמסר" בלי זמן מסירה, או משלוח בסטטוס "נכשל" עם זמן מסירה. מדגים **קבוצות מקוננות**: OR של שתי קבוצות AND.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "group", "logic": "OR", "children": [
    { "type": "group", "logic": "AND", "children": [
      { "type": "rule", "field": "Status", "operator": "eq", "compareTo": { "kind": "value", "value": "Delivered" } },
      { "type": "rule", "field": "DeliveredAt", "operator": "isEmpty" } ] },
    { "type": "group", "logic": "AND", "children": [
      { "type": "rule", "field": "Status", "operator": "eq", "compareTo": { "kind": "value", "value": "Failed" } },
      { "type": "rule", "field": "DeliveredAt", "operator": "isNotEmpty" } ] } ] } }
```

**תוצאה צפויה:** 2 חריגות, משלוחים 15 ו-16.

### 11. זמן יציאה עתידי

**תיאור עסקי:** משלוחים שזמן היציאה שלהם מאוחר מהרגע הנוכחי.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "rule", "field": "DispatchedAt", "operator": "gt", "compareTo": { "kind": "now" } } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 17.

### 12. משקל חבילה שלילי

**תיאור עסקי:** משלוחים עם משקל חבילה קטן מאפס.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "rule", "field": "PackageWeightKg", "operator": "lt", "compareTo": { "kind": "value", "value": 0 } } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 18.

### 13. משלוח ברחפן שנמצא בתחזוקה

**תיאור עסקי:** משלוחים שבוצעו ברחפן שהסטטוס שלו "בתחזוקה".

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "rule", "field": "Drone.Status", "operator": "eq", "compareTo": { "kind": "value", "value": "Maintenance" } } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 19.

### 14. משלוח ע"י מפעיל לא פעיל

**תיאור עסקי:** משלוחים שבוצעו על ידי מפעיל שאינו פעיל.

```json
{ "version": 1, "rootEntity": "Delivery", "condition":
  { "type": "rule", "field": "Pilot.IsActive", "operator": "eq", "compareTo": { "kind": "value", "value": false } } }
```

**תוצאה צפויה:** 1 חריגה, משלוח 20.

## בקרות "חיות" לדמו

שלוש הבקרות הבאות **לא קיימות** בנתוני הדוגמה, ומיועדות להגדרה במהלך הדמו דרך מסך הגדרת הבקרות.
הן מראות שאפשר להוסיף בקרה חדשה בלי שינוי קוד. את התוצאות הצפויות בדקתי מול נתוני הדוגמה.

### א. לקוח ללא כתובת

**תיאור עסקי:** לקוחות שאין להם כתובת מסירה. בנתונים יש לקוח כזה, שאף אחת מ-14 הבקרות לא מכסה, בדיוק לצורך הדמו.

**במסך:** ישות "לקוח" ← שדה "כתובת" ← אופרטור "ריק".

```json
{ "version": 1, "rootEntity": "Customer", "condition":
  { "type": "rule", "field": "Address", "operator": "isEmpty" } }
```

**תוצאה צפויה:** 1 חריגה, לקוח 8. סיבה: `כתובת: חסר ערך`.

### ב. רחפן בשירות שלא עבר תחזוקה יותר משנה

**תיאור עסקי:** רחפנים בשירות שהתחזוקה האחרונה שלהם הייתה לפני יותר מ-365 ימים. מדגים תאריך יחסי עם היסט.

**במסך:** ישות "רחפן" ← "סטטוס רחפן" הוא "בשירות" **וגם** "תחזוקה אחרונה" לפני "היום − 365 ימים".

```json
{ "version": 1, "rootEntity": "Drone", "condition":
  { "type": "group", "logic": "AND", "children": [
    { "type": "rule", "field": "Status", "operator": "eq", "compareTo": { "kind": "value", "value": "Active" } },
    { "type": "rule", "field": "LastMaintenance", "operator": "lt", "compareTo": { "kind": "today", "offsetDays": -365 } } ] } }
```

**תוצאה צפויה:** 2 חריגות, רחפנים 4 ו-5.
**מקרה גבול:** גם רחפן 7 לא עבר תחזוקה יותר משנה, אבל הוא הוצא משירות, ולכן לא נתפס.

### ג. התראת מזג אוויר לא סבירה (אחרי `06`)

**תיאור עסקי:** התראות מזג אוויר שמסתיימות לפני שהתחילו, או עם מהירות רוח לא סבירה (מעל 200 קמ"ש).
הבקרה מוגדרת על ישות **חדשה** שנוספה דרך Metadata בלבד. זו הדגמת הגנריות המרכזית.

**הכנה:**
```powershell
sqlcmd -S localhost\SQLEXPRESS -E -C -b -f 65001 -i .\database\06_demo_add_weather_alerts.sql
Invoke-RestMethod -Method Post http://localhost:5149/api/metadata/refresh   # רענון הקטלוג ב-API
```

**במסך:** ישות "התראת מזג אוויר" ← קבוצת **או**:
"סיום התראה" לפני השדה "תחילת התראה", **או** "מהירות רוח" גדול מ-200.

```json
{ "version": 1, "rootEntity": "WeatherAlert", "condition":
  { "type": "group", "logic": "OR", "children": [
    { "type": "rule", "field": "EndsAt", "operator": "lt", "compareTo": { "kind": "field", "field": "StartsAt" } },
    { "type": "rule", "field": "WindSpeedKmh", "operator": "gt", "compareTo": { "kind": "value", "value": 200 } } ] } }
```

**תוצאה צפויה:** 2 חריגות. התראה 3 מסתיימת לפני שהתחילה, ובהתראה 4 מהירות הרוח היא 250 קמ"ש.
כל אחת מוצגת עם הסיבה שלה בלבד, כי זו קבוצת OR.

**הרחבה בדמו:** הקשר "המוקד של ההתראה" זמין מיד, למשל `Hub.IsActive` או `Hub.City`.
לא נכתבה שורת קוד אחת: נוספו רק טבלה ושורות ב-`meta.*`.
