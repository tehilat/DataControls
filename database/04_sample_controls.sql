/* =====================================================================
   04_sample_controls.sql — 14 sample controls stored as JSON definitions.

   DefinitionJson structure (version 1)
   ------------------------------------
   { "version": 1, "rootEntity": "<EntityKey>", "condition": <node> }

   node    = group | rule
   group   = { "type": "group", "logic": "AND" | "OR", "children": [ <node>, ... ] }
   rule    = { "type": "rule", "field": "<path>", "operator": "<OperatorKey>", "compareTo": <operand> }
             ("compareTo" is omitted for operators with RequiresValue = 0: isEmpty / isNotEmpty)
   path    = FieldKey of the root entity            e.g. "PackageWeightKg"
           | RelationKey(.RelationKey)*.FieldKey    e.g. "Drone.MaxPayloadKg", "Drone.Hub.IsActive"
   operand = { "kind": "value", "value": <json scalar> }
           | { "kind": "field", "field": "<path>" }        (only where AllowFieldCompare = 1)
           | { "kind": "today", "offsetDays": <int> }      (date relative to the run day)
           | { "kind": "now" }                             (date+time of the run)

   Semantics: a record is a VIOLATION when the condition evaluates to TRUE.
   Comparisons against an empty (NULL) value are never true.
   Paths, entity keys and operator keys are resolved only through meta.*.

   Expected results (verified by 99_verify.sql) are noted per control.
   ===================================================================== */
SET NOCOUNT ON;
USE DroneOpsControls;
GO

DELETE FROM ctl.ControlRuns;
DELETE FROM ctl.Controls;
GO

SET IDENTITY_INSERT ctl.Controls ON;

-- Control 1 — expected 1 violation: Delivery 10
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(1, N'משקל חבילה חורג מכושר הנשיאה', N'משלוחים שבהם משקל החבילה גבוה מכושר הנשיאה של הרחפן', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"rule","field":"PackageWeightKg","operator":"gt","compareTo":{"kind":"field","field":"Drone.MaxPayloadKg"}}}',
N'High', N'Manual');

-- Control 2 — expected 1 violation: Delivery 11
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(2, N'מרחק משלוח חורג מטווח הטיסה', N'משלוחים שבהם המרחק גדול מטווח הטיסה של הרחפן', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"rule","field":"DistanceKm","operator":"gt","compareTo":{"kind":"field","field":"Drone.MaxRangeKm"}}}',
N'High', N'Manual');

-- Control 3 — expected 1 violation: Delivery 12
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(3, N'זמן מסירה לפני זמן היציאה', N'משלוחים שבהם זמן המסירה מוקדם מזמן היציאה', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"rule","field":"DeliveredAt","operator":"lt","compareTo":{"kind":"field","field":"DispatchedAt"}}}',
N'Medium', N'Manual');

-- Control 4 — expected 2 violations: Deliveries 13, 14
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(4, N'מפעיל ללא רישיון בתוקף', N'משלוחים שבוצעו ע"י מפעיל ללא רישיון, או שהרישיון שלו פג לפני היציאה', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"group","logic":"OR","children":[
    {"type":"rule","field":"Pilot.LicenseNumber","operator":"isEmpty"},
    {"type":"rule","field":"Pilot.LicenseExpiry","operator":"lt","compareTo":{"kind":"field","field":"DispatchedAt"}}
  ]}}',
N'High', N'Manual');

-- Control 5 — expected 1 violation: Delivery 15
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(5, N'משלוח שנמסר ללא זמן מסירה', N'משלוחים בסטטוס "נמסר" שאין להם זמן מסירה', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"group","logic":"AND","children":[
    {"type":"rule","field":"Status","operator":"eq","compareTo":{"kind":"value","value":"Delivered"}},
    {"type":"rule","field":"DeliveredAt","operator":"isEmpty"}
  ]}}',
N'Medium', N'Manual');

-- Control 6 — expected 1 violation: Drone 9
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(6, N'רחפן פעיל במוקד סגור', N'רחפנים בשירות שמשויכים למוקד שאינו פעיל', N'Drone',
N'{"version":1,"rootEntity":"Drone","condition":
  {"type":"group","logic":"AND","children":[
    {"type":"rule","field":"Status","operator":"eq","compareTo":{"kind":"value","value":"Active"}},
    {"type":"rule","field":"Hub.IsActive","operator":"eq","compareTo":{"kind":"value","value":false}}
  ]}}',
N'Medium', N'Manual');

-- Control 7 — expected 2 violations: Drones 4, 5  (Drone 7 is retired -> not caught)
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(7, N'רחפן בשירות עם תחזוקה באיחור', N'רחפנים בשירות שמועד התחזוקה שלהם כבר עבר', N'Drone',
N'{"version":1,"rootEntity":"Drone","condition":
  {"type":"group","logic":"AND","children":[
    {"type":"rule","field":"Status","operator":"eq","compareTo":{"kind":"value","value":"Active"}},
    {"type":"rule","field":"NextMaintenanceDue","operator":"lt","compareTo":{"kind":"today","offsetDays":0}}
  ]}}',
N'High', N'Manual');

-- Control 8 — expected 1 violation: Customer 6  (Customer 7 has email -> not caught)
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(8, N'לקוח ללא פרטי קשר', N'לקוחות שאין להם טלפון וגם אין להם אימייל', N'Customer',
N'{"version":1,"rootEntity":"Customer","condition":
  {"type":"group","logic":"AND","children":[
    {"type":"rule","field":"Phone","operator":"isEmpty"},
    {"type":"rule","field":"Email","operator":"isEmpty"}
  ]}}',
N'Low', N'Manual');

-- Control 9 — expected 1 violation: Delivery 9  (Delivery 5: light and short -> not caught)
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(9, N'משלוח כבד וארוך ברחפן עם תחזוקה באיחור', N'משלוחים מעל 5 ק"ג ומעל 20 ק"מ ברחפן שמועד התחזוקה שלו עבר', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"group","logic":"AND","children":[
    {"type":"rule","field":"PackageWeightKg","operator":"gt","compareTo":{"kind":"value","value":5}},
    {"type":"rule","field":"DistanceKm","operator":"gt","compareTo":{"kind":"value","value":20}},
    {"type":"rule","field":"Drone.NextMaintenanceDue","operator":"lt","compareTo":{"kind":"today","offsetDays":0}}
  ]}}',
N'High', N'Manual');

-- Control 10 — expected 2 violations: Deliveries 15, 16  (nested groups: OR of two ANDs)
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(10, N'סטטוס משלוח לא עקבי', N'"נמסר" ללא זמן מסירה, או "נכשל" עם זמן מסירה', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"group","logic":"OR","children":[
    {"type":"group","logic":"AND","children":[
      {"type":"rule","field":"Status","operator":"eq","compareTo":{"kind":"value","value":"Delivered"}},
      {"type":"rule","field":"DeliveredAt","operator":"isEmpty"}
    ]},
    {"type":"group","logic":"AND","children":[
      {"type":"rule","field":"Status","operator":"eq","compareTo":{"kind":"value","value":"Failed"}},
      {"type":"rule","field":"DeliveredAt","operator":"isNotEmpty"}
    ]}
  ]}}',
N'Medium', N'Manual');

-- Control 11 — expected 1 violation: Delivery 17
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(11, N'זמן יציאה עתידי', N'משלוחים שזמן היציאה שלהם מאוחר מהרגע הנוכחי', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"rule","field":"DispatchedAt","operator":"gt","compareTo":{"kind":"now"}}}',
N'Low', N'Manual');

-- Control 12 — expected 1 violation: Delivery 18
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(12, N'משקל חבילה שלילי', N'משלוחים עם משקל חבילה קטן מאפס', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"rule","field":"PackageWeightKg","operator":"lt","compareTo":{"kind":"value","value":0}}}',
N'Medium', N'Manual');

-- Control 13 — expected 1 violation: Delivery 19
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(13, N'משלוח ברחפן שנמצא בתחזוקה', N'משלוחים שבוצעו ברחפן שהסטטוס שלו "בתחזוקה"', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"rule","field":"Drone.Status","operator":"eq","compareTo":{"kind":"value","value":"Maintenance"}}}',
N'High', N'Manual');

-- Control 14 — expected 1 violation: Delivery 20
INSERT INTO ctl.Controls (ControlId, Name, Description, RootEntityKey, DefinitionJson, Severity, Source) VALUES
(14, N'משלוח ע"י מפעיל לא פעיל', N'משלוחים שבוצעו ע"י מפעיל שאינו פעיל', N'Delivery',
N'{"version":1,"rootEntity":"Delivery","condition":
  {"type":"rule","field":"Pilot.IsActive","operator":"eq","compareTo":{"kind":"value","value":false}}}',
N'Medium', N'Manual');

SET IDENTITY_INSERT ctl.Controls OFF;
GO

PRINT N'04_sample_controls.sql completed.';
