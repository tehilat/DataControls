/* =====================================================================
   03_metadata.sql — the business catalog (meta schema).

   Maps Hebrew business terms to physical tables / columns / joins.
   The UI and the AI only ever see EntityKey / FieldKey / RelationKey and
   the Hebrew display names. Physical identifiers here act as a whitelist
   for the query builder. NO SQL is stored in meta — translation to SQL
   exists only in code.

   Relations are many-to-one only (child -> parent), so joins never
   multiply rows of the root entity:
     Delivery -> Drone -> Hub,  Delivery -> Pilot,  Delivery -> Customer

   Re-runnable: clears meta (and ctl, which references meta.Entities).
   ===================================================================== */
SET NOCOUNT ON;
USE DroneOpsControls;
GO

DELETE FROM ctl.ControlRuns;
DELETE FROM ctl.Controls;
DELETE FROM meta.DataTypeOperators;
DELETE FROM meta.Operators;
DELETE FROM meta.Relations;
DELETE FROM meta.FieldOptions;
DELETE FROM meta.Fields;
DELETE FROM meta.Entities;
GO

/* ---------------- Entities ---------------- */
INSERT INTO meta.Entities (EntityKey, DisplayName, DisplayNamePlural, Description, SchemaName, TableName, PrimaryKeyColumn, DisplayColumn, IsActive) VALUES
 (N'Delivery', N'משלוח', N'משלוחים', N'משלוח חבילה ללקוח באמצעות רחפן ומפעיל',          N'ops', N'Deliveries', N'DeliveryId', N'DeliveryId',   1),
 (N'Drone',    N'רחפן',  N'רחפנים',  N'רחפן בצי המשלוחים, משויך למוקד',                 N'ops', N'Drones',     N'DroneId',    N'SerialNumber', 1),
 (N'Hub',      N'מוקד',  N'מוקדים',  N'מוקד הפעלה שממנו יוצאים הרחפנים',                N'ops', N'Hubs',       N'HubId',      N'Name',         1),
 (N'Pilot',    N'מפעיל', N'מפעילים', N'מפעיל רחפנים מורשה שאחראי על המשלוח',             N'ops', N'Pilots',     N'PilotId',    N'FullName',     1),
 (N'Customer', N'לקוח',  N'לקוחות',  N'לקוח שמקבל משלוחים',                              N'ops', N'Customers',  N'CustomerId', N'FullName',     1);

/* ---------------- Fields ---------------- */
INSERT INTO meta.Fields (EntityKey, FieldKey, DisplayName, Description, ColumnName, DataType, Unit, ShowInResults, SortOrder) VALUES
 -- Delivery
 (N'Delivery', N'DeliveryId',      N'מספר משלוח',     N'מזהה המשלוח',                          N'DeliveryId',      N'number',   NULL,    1, 10),
 (N'Delivery', N'PackageWeightKg', N'משקל חבילה',     N'משקל החבילה שנשלחה',                  N'PackageWeightKg', N'number',   N'ק"ג',  1, 20),
 (N'Delivery', N'DistanceKm',      N'מרחק משלוח',     N'מרחק הטיסה מהמוקד ללקוח',             N'DistanceKm',      N'number',   N'ק"מ',  1, 30),
 (N'Delivery', N'DispatchedAt',    N'זמן יציאה',      N'מועד יציאת הרחפן מהמוקד',             N'DispatchedAt',    N'datetime', NULL,    1, 40),
 (N'Delivery', N'DeliveredAt',     N'זמן מסירה',      N'מועד מסירת החבילה ללקוח',             N'DeliveredAt',     N'datetime', NULL,    1, 50),
 (N'Delivery', N'Status',          N'סטטוס משלוח',    N'מצב המשלוח הנוכחי',                   N'Status',          N'enum',     NULL,    1, 60),
 -- Drone
 (N'Drone', N'DroneId',            N'מספר רחפן',          N'מזהה הרחפן',                       N'DroneId',            N'number', NULL,   0, 10),
 (N'Drone', N'SerialNumber',       N'מספר סידורי',        N'המספר הסידורי של הרחפן',           N'SerialNumber',       N'text',   NULL,   1, 20),
 (N'Drone', N'Model',              N'דגם',                N'דגם הרחפן',                        N'Model',              N'text',   NULL,   1, 30),
 (N'Drone', N'MaxPayloadKg',       N'כושר נשיאה',         N'המשקל המרבי שהרחפן יכול לשאת',     N'MaxPayloadKg',       N'number', N'ק"ג', 1, 40),
 (N'Drone', N'MaxRangeKm',         N'טווח טיסה',          N'המרחק המרבי שהרחפן יכול לטוס',     N'MaxRangeKm',         N'number', N'ק"מ', 1, 50),
 (N'Drone', N'LastMaintenance',    N'תחזוקה אחרונה',      N'תאריך התחזוקה האחרונה',            N'LastMaintenance',    N'date',   NULL,   1, 60),
 (N'Drone', N'NextMaintenanceDue', N'מועד תחזוקה הבא',    N'התאריך שעד אליו יש לבצע תחזוקה',   N'NextMaintenanceDue', N'date',   NULL,   1, 70),
 (N'Drone', N'Status',             N'סטטוס רחפן',         N'מצב הרחפן: בשירות, בתחזוקה או הוצא משירות', N'Status',    N'enum',   NULL,   1, 80),
 -- Hub
 (N'Hub', N'HubId',    N'מספר מוקד', N'מזהה המוקד',              N'HubId',    N'number',  NULL, 0, 10),
 (N'Hub', N'Name',     N'שם מוקד',   N'שם המוקד',                N'Name',     N'text',    NULL, 1, 20),
 (N'Hub', N'City',     N'עיר מוקד',  N'העיר שבה נמצא המוקד',     N'City',     N'text',    NULL, 1, 30),
 (N'Hub', N'IsActive', N'מוקד פעיל', N'האם המוקד פתוח ופעיל',    N'IsActive', N'boolean', NULL, 1, 40),
 -- Pilot
 (N'Pilot', N'PilotId',       N'מספר מפעיל',     N'מזהה המפעיל',                 N'PilotId',       N'number',  NULL, 0, 10),
 (N'Pilot', N'FullName',      N'שם מפעיל',       N'שם המפעיל המלא',              N'FullName',      N'text',    NULL, 1, 20),
 (N'Pilot', N'LicenseNumber', N'מספר רישיון',    N'מספר רישיון ההפעלה',          N'LicenseNumber', N'text',    NULL, 1, 30),
 (N'Pilot', N'LicenseExpiry', N'תוקף רישיון',    N'התאריך שבו פג תוקף הרישיון',  N'LicenseExpiry', N'date',    NULL, 1, 40),
 (N'Pilot', N'IsActive',      N'מפעיל פעיל',     N'האם המפעיל פעיל כיום',        N'IsActive',      N'boolean', NULL, 1, 50),
 -- Customer
 (N'Customer', N'CustomerId', N'מספר לקוח', N'מזהה הלקוח',          N'CustomerId', N'number', NULL, 0, 10),
 (N'Customer', N'FullName',   N'שם לקוח',   N'שם הלקוח המלא',       N'FullName',   N'text',   NULL, 1, 20),
 (N'Customer', N'Phone',      N'טלפון',     N'מספר הטלפון של הלקוח', N'Phone',      N'text',   NULL, 1, 30),
 (N'Customer', N'Email',      N'אימייל',    N'כתובת האימייל של הלקוח', N'Email',    N'text',   NULL, 1, 40),
 (N'Customer', N'City',       N'עיר',       N'עיר המגורים של הלקוח', N'City',       N'text',   NULL, 1, 50),
 (N'Customer', N'Address',    N'כתובת',     N'כתובת המסירה של הלקוח', N'Address',   N'text',   NULL, 1, 60);

/* ---------------- FieldOptions (enum values) ---------------- */
INSERT INTO meta.FieldOptions (FieldId, OptionValue, DisplayName, SortOrder)
SELECT f.FieldId, v.OptionValue, v.DisplayName, v.SortOrder
FROM (VALUES
    (N'Delivery', N'Status', N'Pending',     N'ממתין',          1),
    (N'Delivery', N'Status', N'InTransit',   N'בדרך',           2),
    (N'Delivery', N'Status', N'Delivered',   N'נמסר',           3),
    (N'Delivery', N'Status', N'Failed',      N'נכשל',           4),
    (N'Drone',    N'Status', N'Active',      N'בשירות',         1),
    (N'Drone',    N'Status', N'Maintenance', N'בתחזוקה',        2),
    (N'Drone',    N'Status', N'Retired',     N'הוצא משירות',    3)
) AS v (EntityKey, FieldKey, OptionValue, DisplayName, SortOrder)
JOIN meta.Fields f ON f.EntityKey = v.EntityKey AND f.FieldKey = v.FieldKey;

/* ---------------- Relations (many-to-one only) ---------------- */
INSERT INTO meta.Relations (FromEntityKey, RelationKey, ToEntityKey, FromColumn, ToColumn, DisplayName) VALUES
 (N'Delivery', N'Drone',    N'Drone',    N'DroneId',    N'DroneId',    N'הרחפן של המשלוח'),
 (N'Delivery', N'Pilot',    N'Pilot',    N'PilotId',    N'PilotId',    N'המפעיל של המשלוח'),
 (N'Delivery', N'Customer', N'Customer', N'CustomerId', N'CustomerId', N'הלקוח של המשלוח'),
 (N'Drone',    N'Hub',      N'Hub',      N'HubId',      N'HubId',      N'המוקד של הרחפן');

/* ---------------- Operators ---------------- */
INSERT INTO meta.Operators (OperatorKey, DisplayName, RequiresValue, SortOrder) VALUES
 (N'eq',         N'שווה ל',           1, 1),
 (N'neq',        N'שונה מ',           1, 2),
 (N'gt',         N'גדול מ',           1, 3),
 (N'gte',        N'גדול או שווה ל',   1, 4),
 (N'lt',         N'קטן מ',            1, 5),
 (N'lte',        N'קטן או שווה ל',    1, 6),
 (N'contains',   N'מכיל',             1, 7),
 (N'isEmpty',    N'ריק',              0, 8),
 (N'isNotEmpty', N'לא ריק',           0, 9);

/* ---------------- DataTypeOperators (which operator is valid for which type) ---------------- */
INSERT INTO meta.DataTypeOperators (DataType, OperatorKey, DisplayNameOverride, AllowFieldCompare) VALUES
 -- number
 (N'number', N'eq',         NULL, 1),
 (N'number', N'neq',        NULL, 1),
 (N'number', N'gt',         NULL, 1),
 (N'number', N'gte',        NULL, 1),
 (N'number', N'lt',         NULL, 1),
 (N'number', N'lte',        NULL, 1),
 (N'number', N'isEmpty',    NULL, 0),
 (N'number', N'isNotEmpty', NULL, 0),
 -- text
 (N'text', N'eq',         NULL, 1),
 (N'text', N'neq',        NULL, 1),
 (N'text', N'contains',   NULL, 0),
 (N'text', N'isEmpty',    NULL, 0),
 (N'text', N'isNotEmpty', NULL, 0),
 -- date
 (N'date', N'eq',         N'בתאריך',              1),
 (N'date', N'neq',        N'לא בתאריך',           1),
 (N'date', N'gt',         N'אחרי',                1),
 (N'date', N'gte',        N'באותו מועד או אחרי',  1),
 (N'date', N'lt',         N'לפני',                1),
 (N'date', N'lte',        N'באותו מועד או לפני',  1),
 (N'date', N'isEmpty',    NULL,                   0),
 (N'date', N'isNotEmpty', NULL,                   0),
 -- datetime
 (N'datetime', N'eq',         N'בדיוק ב',             1),
 (N'datetime', N'neq',        N'לא בדיוק ב',          1),
 (N'datetime', N'gt',         N'אחרי',                1),
 (N'datetime', N'gte',        N'באותו מועד או אחרי',  1),
 (N'datetime', N'lt',         N'לפני',                1),
 (N'datetime', N'lte',        N'באותו מועד או לפני',  1),
 (N'datetime', N'isEmpty',    NULL,                   0),
 (N'datetime', N'isNotEmpty', NULL,                   0),
 -- boolean
 (N'boolean', N'eq',  N'הוא',   0),
 (N'boolean', N'neq', N'אינו',  0),
 -- enum
 (N'enum', N'eq',  N'הוא',   0),
 (N'enum', N'neq', N'אינו',  0);
GO

PRINT N'03_metadata.sql completed.';
