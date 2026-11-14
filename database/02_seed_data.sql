/* =====================================================================
   02_seed_data.sql — business data (valid + intentionally invalid rows).

   Date policy (so the controls stay correct when the demo runs weeks
   after the seed):
     * All dates are relative to @today / @now — no fixed dates.
     * Future dates in VALID data (NextMaintenanceDue, LicenseExpiry)
       are at least +365 days ahead.
     * The future dispatch time of delivery 17 (control 11) is at least
       +180 days ahead.
     * Past dates in INVALID data (overdue maintenance, expired license)
       are at least 30 days back.

   Every invalid row is marked with:  -- [שגוי] בקרה N: <explanation>
   Each invalid case is isolated: a bad row should be caught only by its
   own control (unless stated otherwise).
   ===================================================================== */
SET NOCOUNT ON;
USE DroneOpsControls;
GO

DELETE FROM ops.Deliveries;
DELETE FROM ops.Drones;
DELETE FROM ops.Pilots;
DELETE FROM ops.Customers;
DELETE FROM ops.Hubs;
GO

DECLARE @today DATE         = CAST(SYSDATETIME() AS DATE);
DECLARE @now   DATETIME2(0) = CAST(SYSDATETIME() AS DATETIME2(0));

/* ---------------- Hubs ---------------- */
SET IDENTITY_INSERT ops.Hubs ON;
INSERT INTO ops.Hubs (HubId, Name, City, IsActive) VALUES
 (1, N'מוקד מרכז',   N'תל אביב',  1),
 (2, N'מוקד צפון',   N'חיפה',     1),
 (3, N'מוקד ירושלים', N'ירושלים',  1),
 (4, N'מוקד דרום',   N'באר שבע',  1),
 (5, N'מוקד אילת',   N'אילת',     0);   -- closed hub (used by control 6)
SET IDENTITY_INSERT ops.Hubs OFF;

/* ---------------- Drones ---------------- */
SET IDENTITY_INSERT ops.Drones ON;
INSERT INTO ops.Drones (DroneId, SerialNumber, HubId, Model, MaxPayloadKg, MaxRangeKm, LastMaintenance, NextMaintenanceDue, Status) VALUES
 (1,  N'DRN-1001', 1, N'SkyLift 500',  5.00, 30.00, DATEADD(DAY, -20,  @today), DATEADD(DAY, 400, @today), N'Active'),
 (2,  N'DRN-1002', 1, N'SkyLift 800',  8.00, 40.00, DATEADD(DAY, -15,  @today), DATEADD(DAY, 380, @today), N'Active'),
 (3,  N'DRN-1003', 2, N'AeroMini 300', 3.00, 15.00, DATEADD(DAY, -10,  @today), DATEADD(DAY, 420, @today), N'Active'),
 (4,  N'DRN-1004', 2, N'HeavyHawk 10', 10.00, 50.00, DATEADD(DAY, -400, @today), DATEADD(DAY, -40, @today), N'Active'),   -- [שגוי] בקרה 7: רחפן פעיל שמועד התחזוקה שלו עבר
 (5,  N'DRN-1005', 3, N'SkyLift 600',  6.00, 25.00, DATEADD(DAY, -390, @today), DATEADD(DAY, -35, @today), N'Active'),   -- [שגוי] בקרה 7: רחפן פעיל שמועד התחזוקה שלו עבר
 (6,  N'DRN-1006', 3, N'SkyLift 500',  5.00, 30.00, DATEADD(DAY, -5,   @today), DATEADD(DAY, 365, @today), N'Maintenance'),
 (7,  N'DRN-1007', 4, N'AeroMini 400', 4.00, 20.00, DATEADD(DAY, -500, @today), DATEADD(DAY, -100, @today), N'Retired'), -- boundary: overdue maintenance but retired -> NOT caught by control 7
 (8,  N'DRN-1008', 4, N'SkyLift 700',  7.00, 35.00, DATEADD(DAY, -30,  @today), DATEADD(DAY, 390, @today), N'Active'),
 (9,  N'DRN-1009', 5, N'SkyLift 500',  5.00, 30.00, DATEADD(DAY, -25,  @today), DATEADD(DAY, 410, @today), N'Active'),   -- [שגוי] בקרה 6: רחפן פעיל במוקד סגור
 (10, N'DRN-1010', 5, N'AeroMini 300', 3.00, 15.00, DATEADD(DAY, -200, @today), DATEADD(DAY, 450, @today), N'Retired'); -- boundary: closed hub but retired -> NOT caught by control 6
SET IDENTITY_INSERT ops.Drones OFF;

/* ---------------- Pilots ---------------- */
SET IDENTITY_INSERT ops.Pilots ON;
INSERT INTO ops.Pilots (PilotId, FullName, LicenseNumber, LicenseExpiry, IsActive) VALUES
 (1, N'דנה לוי',      N'LIC-2001', DATEADD(DAY, 500, @today), 1),
 (2, N'יוסי כהן',     N'LIC-2002', DATEADD(DAY, 450, @today), 1),
 (3, N'מיכל אברהם',   N'LIC-2003', DATEADD(DAY, 700, @today), 1),
 (4, N'אורי מזרחי',   N'LIC-2004', DATEADD(DAY, 400, @today), 0),   -- inactive pilot (used by delivery 20, control 14)
 (5, N'נועה פרץ',     N'LIC-2005', DATEADD(DAY, -45, @today), 1),   -- expired license (used by delivery 13, control 4)
 (6, N'עידו שפירא',   NULL,        NULL,                      1);   -- no license at all (used by delivery 14, control 4)
SET IDENTITY_INSERT ops.Pilots OFF;

/* ---------------- Customers ---------------- */
SET IDENTITY_INSERT ops.Customers ON;
INSERT INTO ops.Customers (CustomerId, FullName, Phone, Email, City, Address) VALUES
 (1, N'רונית גולן',    N'050-1234567', N'ronit@example.com',  N'תל אביב',  N'דיזנגוף 10'),
 (2, N'אבי ביטון',     N'052-2345678', N'avi@example.com',    N'חיפה',     N'הנביאים 5'),
 (3, N'שירה כץ',       N'054-3456789', N'shira@example.com',  N'ירושלים',  N'יפו 120'),
 (4, N'משה דהן',       N'053-4567890', N'moshe@example.com',  N'באר שבע',  N'רגר 30'),
 (5, N'תמר אלון',      N'058-5678901', N'tamar@example.com',  N'רמת גן',   N'ביאליק 7'),
 (6, N'גיל רוזן',      NULL,           NULL,                  N'חולון',    N'סוקולוב 22'),  -- [שגוי] בקרה 8: לקוח בלי טלפון ובלי אימייל
 (7, N'ליאת חדד',      NULL,           N'liat@example.com',   N'נתניה',    N'הרצל 15'),     -- boundary: email only -> NOT caught by control 8
 (8, N'עומר נחום',     N'050-8901234', N'omer@example.com',   N'הרצליה',   NULL);           -- boundary: no address, not covered by any control (live demo)
SET IDENTITY_INSERT ops.Customers OFF;

/* ---------------- Deliveries ---------------- */
SET IDENTITY_INSERT ops.Deliveries ON;
INSERT INTO ops.Deliveries (DeliveryId, DroneId, PilotId, CustomerId, PackageWeightKg, DistanceKm, DispatchedAt, DeliveredAt, Status) VALUES
 -- valid
 (1,  1, 1, 1,  2.00, 10.00, DATEADD(DAY, -10, @now), DATEADD(MINUTE, 25, DATEADD(DAY, -10, @now)), N'Delivered'),
 (2,  2, 2, 2,  5.00, 20.00, DATEADD(DAY, -9,  @now), DATEADD(MINUTE, 40, DATEADD(DAY, -9,  @now)), N'Delivered'),
 (3,  3, 3, 3,  1.50,  8.00, DATEADD(DAY, -8,  @now), DATEADD(MINUTE, 15, DATEADD(DAY, -8,  @now)), N'Delivered'),
 (4,  8, 1, 4,  4.00, 18.00, DATEADD(DAY, -7,  @now), DATEADD(MINUTE, 35, DATEADD(DAY, -7,  @now)), N'Delivered'),
 (5,  5, 2, 5,  2.00,  8.00, DATEADD(DAY, -6,  @now), DATEADD(MINUTE, 20, DATEADD(DAY, -6,  @now)), N'Delivered'),  -- boundary: overdue drone but light and short -> NOT caught by control 9
 (6,  1, 3, 7,  3.00, 12.00, DATEADD(MINUTE, -20, @now), NULL,                                      N'InTransit'),
 (7,  2, 1, 8,  1.00,  5.00, NULL,                     NULL,                                        N'Pending'),
 (8,  8, 2, 1,  6.00, 30.00, DATEADD(DAY, -5,  @now), DATEADD(MINUTE, 50, DATEADD(DAY, -5,  @now)), N'Delivered'),  -- boundary: heavy and long but drone maintenance is fine -> NOT caught by control 9
 -- invalid
 (9,  4, 3, 2,  6.00, 25.00, DATEADD(DAY, -4,  @now), DATEADD(MINUTE, 45, DATEADD(DAY, -4,  @now)), N'Delivered'),  -- [שגוי] בקרה 9: מעל 5 ק"ג ומעל 20 ק"מ ברחפן עם תחזוקה באיחור
 (10, 3, 1, 3,  4.50, 10.00, DATEADD(DAY, -4,  @now), DATEADD(MINUTE, 30, DATEADD(DAY, -4,  @now)), N'Delivered'),  -- [שגוי] בקרה 1: 4.5 ק"ג ברחפן שכושר הנשיאה שלו 3 ק"ג
 (11, 1, 2, 4,  2.00, 35.00, DATEADD(DAY, -3,  @now), DATEADD(MINUTE, 55, DATEADD(DAY, -3,  @now)), N'Delivered'),  -- [שגוי] בקרה 2: 35 ק"מ ברחפן שטווח הטיסה שלו 30 ק"מ
 (12, 2, 3, 5,  3.00, 15.00, DATEADD(DAY, -3,  @now), DATEADD(HOUR, -1,  DATEADD(DAY, -3,  @now)), N'Delivered'),  -- [שגוי] בקרה 3: זמן מסירה שעה לפני זמן היציאה
 (13, 8, 5, 1,  2.50, 12.00, DATEADD(DAY, -2,  @now), DATEADD(MINUTE, 30, DATEADD(DAY, -2,  @now)), N'Delivered'),  -- [שגוי] בקרה 4: רישיון המפעיל פג לפני היציאה
 (14, 1, 6, 2,  1.50,  9.00, DATEADD(DAY, -2,  @now), DATEADD(MINUTE, 20, DATEADD(DAY, -2,  @now)), N'Delivered'),  -- [שגוי] בקרה 4: מפעיל בלי רישיון
 (15, 2, 1, 3,  2.00, 11.00, DATEADD(DAY, -1,  @now), NULL,                                        N'Delivered'),  -- [שגוי] בקרה 5 + בקרה 10: סטטוס "נמסר" ללא זמן מסירה
 (16, 8, 2, 4,  3.50, 14.00, DATEADD(DAY, -1,  @now), DATEADD(MINUTE, 40, DATEADD(DAY, -1,  @now)), N'Failed'),     -- [שגוי] בקרה 10: סטטוס "נכשל" עם זמן מסירה
 (17, 1, 3, 5,  2.00, 10.00, DATEADD(DAY, 200, @now), NULL,                                        N'Pending'),    -- [שגוי] בקרה 11: זמן יציאה עתידי
 (18, 2, 1, 1, -1.50,  7.00, DATEADD(DAY, -6,  @now), DATEADD(MINUTE, 15, DATEADD(DAY, -6,  @now)), N'Delivered'),  -- [שגוי] בקרה 12: משקל חבילה שלילי
 (19, 6, 2, 2,  2.00, 10.00, DATEADD(DAY, -1,  @now), DATEADD(MINUTE, 25, DATEADD(DAY, -1,  @now)), N'Delivered'),  -- [שגוי] בקרה 13: משלוח ברחפן שנמצא בתחזוקה
 (20, 3, 4, 3,  1.00,  5.00, DATEADD(DAY, -5,  @now), DATEADD(MINUTE, 10, DATEADD(DAY, -5,  @now)), N'Delivered'),  -- [שגוי] בקרה 14: משלוח ע"י מפעיל לא פעיל
 -- valid
 (21, 3, 1, 4,  2.00, 10.00, DATEADD(DAY, -2,  @now), NULL,                                        N'Failed'),
 (22, 8, 3, 5,  3.00, 15.00, DATEADD(MINUTE, -45, @now), NULL,                                      N'InTransit');
SET IDENTITY_INSERT ops.Deliveries OFF;
GO

PRINT N'02_seed_data.sql completed.';
