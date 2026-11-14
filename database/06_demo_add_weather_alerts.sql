/* =====================================================================
   06_demo_add_weather_alerts.sql — genericity demo.

   Adds a brand new business entity ("התראת מזג אוויר") using a table +
   metadata rows ONLY. No C#, Python or Angular change is needed: the new
   entity, its fields and its relation to Hub appear in the UI and can be
   used in controls immediately. No controls are created here — they are
   defined live in the demo through the UI.

   controls_runner already has SELECT on schema ops, so the new table is
   covered automatically.

   Re-runnable: cleans up its own objects first.
   ===================================================================== */
SET NOCOUNT ON;
USE DroneOpsControls;
GO

/* ---------------- cleanup ---------------- */
DELETE r FROM ctl.ControlRuns r JOIN ctl.Controls c ON c.ControlId = r.ControlId WHERE c.RootEntityKey = N'WeatherAlert';
DELETE FROM ctl.Controls WHERE RootEntityKey = N'WeatherAlert';
DELETE FROM meta.Relations WHERE FromEntityKey = N'WeatherAlert' OR ToEntityKey = N'WeatherAlert';
DELETE o FROM meta.FieldOptions o JOIN meta.Fields f ON f.FieldId = o.FieldId WHERE f.EntityKey = N'WeatherAlert';
DELETE FROM meta.Fields   WHERE EntityKey = N'WeatherAlert';
DELETE FROM meta.Entities WHERE EntityKey = N'WeatherAlert';
DROP TABLE IF EXISTS ops.WeatherAlerts;
GO

/* ---------------- table ---------------- */
CREATE TABLE ops.WeatherAlerts
(
    AlertId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WeatherAlerts PRIMARY KEY,
    HubId        INT               NOT NULL CONSTRAINT FK_WeatherAlerts_Hubs REFERENCES ops.Hubs(HubId),
    Severity     NVARCHAR(10)      NOT NULL CONSTRAINT CK_WeatherAlerts_Severity CHECK (Severity IN (N'Low', N'High', N'Severe')),
    StartsAt     DATETIME2(0)      NOT NULL,
    EndsAt       DATETIME2(0)      NULL,
    WindSpeedKmh DECIMAL(6,2)      NOT NULL
);
CREATE INDEX IX_WeatherAlerts_HubId ON ops.WeatherAlerts(HubId);
GO

/* ---------------- data ---------------- */
DECLARE @now DATETIME2(0) = CAST(SYSDATETIME() AS DATETIME2(0));

SET IDENTITY_INSERT ops.WeatherAlerts ON;
INSERT INTO ops.WeatherAlerts (AlertId, HubId, Severity, StartsAt, EndsAt, WindSpeedKmh) VALUES
 (1, 1, N'Low',    DATEADD(DAY, -3, @now), DATEADD(HOUR, 6,  DATEADD(DAY, -3, @now)),  25.00),
 (2, 2, N'High',   DATEADD(DAY, -1, @now), DATEADD(HOUR, 12, DATEADD(DAY, -1, @now)),  65.00),
 (3, 3, N'Severe', DATEADD(DAY, -2, @now), DATEADD(HOUR, -4, DATEADD(DAY, -2, @now)),  90.00),  -- [שגוי] התראה שמסתיימת לפני שהתחילה
 (4, 4, N'High',   DATEADD(HOUR, -5, @now), DATEADD(HOUR, 10, @now),                   250.00); -- [שגוי] מהירות רוח לא סבירה (מעל 200)
SET IDENTITY_INSERT ops.WeatherAlerts OFF;
GO

/* ---------------- metadata only ---------------- */
INSERT INTO meta.Entities (EntityKey, DisplayName, DisplayNamePlural, Description, SchemaName, TableName, PrimaryKeyColumn, DisplayColumn, IsActive) VALUES
 (N'WeatherAlert', N'התראת מזג אוויר', N'התראות מזג אוויר', N'התראת מזג אוויר שהוצאה עבור מוקד', N'ops', N'WeatherAlerts', N'AlertId', N'AlertId', 1);

INSERT INTO meta.Fields (EntityKey, FieldKey, DisplayName, Description, ColumnName, DataType, Unit, ShowInResults, SortOrder) VALUES
 (N'WeatherAlert', N'AlertId',      N'מספר התראה',  N'מזהה ההתראה',              N'AlertId',      N'number',   NULL,      1, 10),
 (N'WeatherAlert', N'Severity',     N'חומרה',       N'רמת החומרה של ההתראה',     N'Severity',     N'enum',     NULL,      1, 20),
 (N'WeatherAlert', N'StartsAt',     N'תחילת התראה', N'מועד תחילת ההתראה',        N'StartsAt',     N'datetime', NULL,      1, 30),
 (N'WeatherAlert', N'EndsAt',       N'סיום התראה',  N'מועד סיום ההתראה',         N'EndsAt',       N'datetime', NULL,      1, 40),
 (N'WeatherAlert', N'WindSpeedKmh', N'מהירות רוח',  N'מהירות הרוח הצפויה',       N'WindSpeedKmh', N'number',   N'קמ"ש',   1, 50);

INSERT INTO meta.FieldOptions (FieldId, OptionValue, DisplayName, SortOrder)
SELECT f.FieldId, v.OptionValue, v.DisplayName, v.SortOrder
FROM (VALUES (N'Low', N'נמוכה', 1), (N'High', N'גבוהה', 2), (N'Severe', N'חמורה', 3)) AS v (OptionValue, DisplayName, SortOrder)
JOIN meta.Fields f ON f.EntityKey = N'WeatherAlert' AND f.FieldKey = N'Severity';

INSERT INTO meta.Relations (FromEntityKey, RelationKey, ToEntityKey, FromColumn, ToColumn, DisplayName) VALUES
 (N'WeatherAlert', N'Hub', N'Hub', N'HubId', N'HubId', N'המוקד של ההתראה');
GO

PRINT N'06_demo_add_weather_alerts.sql completed.';
