/* =====================================================================
   99_verify.sql — verification with HAND-WRITTEN queries (not the engine).

   For each of the 14 sample controls: expected vs. actual violating IDs.
   Also proves that controls_runner cannot UPDATE ops or SELECT from ctl.
   The engine's integration tests must return exactly the same results.
   ===================================================================== */
SET NOCOUNT ON;
USE DroneOpsControls;
GO

DECLARE @today DATE         = CAST(SYSDATETIME() AS DATE);
DECLARE @now   DATETIME2(0) = CAST(SYSDATETIME() AS DATETIME2(0));

DECLARE @expected TABLE (ControlNumber INT PRIMARY KEY, ExpectedIds NVARCHAR(200));
INSERT INTO @expected VALUES
 (1, N'10'), (2, N'11'), (3, N'12'), (4, N'13,14'), (5, N'15'), (6, N'9'), (7, N'4,5'),
 (8, N'6'), (9, N'9'), (10, N'15,16'), (11, N'17'), (12, N'18'), (13, N'19'), (14, N'20');

DECLARE @actual TABLE (ControlNumber INT, Id INT);

-- 1: package weight > drone payload
INSERT INTO @actual SELECT 1, d.DeliveryId FROM ops.Deliveries d JOIN ops.Drones dr ON dr.DroneId = d.DroneId
 WHERE d.PackageWeightKg > dr.MaxPayloadKg;
-- 2: distance > drone range
INSERT INTO @actual SELECT 2, d.DeliveryId FROM ops.Deliveries d JOIN ops.Drones dr ON dr.DroneId = d.DroneId
 WHERE d.DistanceKm > dr.MaxRangeKm;
-- 3: delivered before dispatched
INSERT INTO @actual SELECT 3, d.DeliveryId FROM ops.Deliveries d
 WHERE d.DeliveredAt < d.DispatchedAt;
-- 4: pilot without license, or license expired before dispatch
INSERT INTO @actual SELECT 4, d.DeliveryId FROM ops.Deliveries d JOIN ops.Pilots p ON p.PilotId = d.PilotId
 WHERE (p.LicenseNumber IS NULL OR p.LicenseNumber = N'') OR p.LicenseExpiry < d.DispatchedAt;
-- 5: delivered without delivery time
INSERT INTO @actual SELECT 5, d.DeliveryId FROM ops.Deliveries d
 WHERE d.Status = N'Delivered' AND d.DeliveredAt IS NULL;
-- 6: active drone in closed hub
INSERT INTO @actual SELECT 6, dr.DroneId FROM ops.Drones dr JOIN ops.Hubs h ON h.HubId = dr.HubId
 WHERE dr.Status = N'Active' AND h.IsActive = 0;
-- 7: active drone with overdue maintenance
INSERT INTO @actual SELECT 7, dr.DroneId FROM ops.Drones dr
 WHERE dr.Status = N'Active' AND dr.NextMaintenanceDue < @today;
-- 8: customer without phone and without email
INSERT INTO @actual SELECT 8, c.CustomerId FROM ops.Customers c
 WHERE (c.Phone IS NULL OR c.Phone = N'') AND (c.Email IS NULL OR c.Email = N'');
-- 9: > 5 kg, > 20 km, drone maintenance overdue
INSERT INTO @actual SELECT 9, d.DeliveryId FROM ops.Deliveries d JOIN ops.Drones dr ON dr.DroneId = d.DroneId
 WHERE d.PackageWeightKg > 5 AND d.DistanceKm > 20 AND dr.NextMaintenanceDue < @today;
-- 10: inconsistent status (nested groups)
INSERT INTO @actual SELECT 10, d.DeliveryId FROM ops.Deliveries d
 WHERE (d.Status = N'Delivered' AND d.DeliveredAt IS NULL)
    OR (d.Status = N'Failed'    AND d.DeliveredAt IS NOT NULL);
-- 11: dispatch time in the future
INSERT INTO @actual SELECT 11, d.DeliveryId FROM ops.Deliveries d
 WHERE d.DispatchedAt > @now;
-- 12: negative package weight
INSERT INTO @actual SELECT 12, d.DeliveryId FROM ops.Deliveries d
 WHERE d.PackageWeightKg < 0;
-- 13: drone in maintenance
INSERT INTO @actual SELECT 13, d.DeliveryId FROM ops.Deliveries d JOIN ops.Drones dr ON dr.DroneId = d.DroneId
 WHERE dr.Status = N'Maintenance';
-- 14: inactive pilot
INSERT INTO @actual SELECT 14, d.DeliveryId FROM ops.Deliveries d JOIN ops.Pilots p ON p.PilotId = d.PilotId
 WHERE p.IsActive = 0;

;WITH agg AS
(
    SELECT e.ControlNumber,
           e.ExpectedIds,
           (SELECT COUNT(*) FROM @actual a WHERE a.ControlNumber = e.ControlNumber) AS ActualCount,
           ISNULL((SELECT STRING_AGG(CAST(a.Id AS NVARCHAR(20)), N',') WITHIN GROUP (ORDER BY a.Id)
                   FROM @actual a WHERE a.ControlNumber = e.ControlNumber), N'') AS ActualIds
    FROM @expected e
)
SELECT agg.ControlNumber,
       c.Name AS ControlName,
       (LEN(agg.ExpectedIds) - LEN(REPLACE(agg.ExpectedIds, N',', N'')) + 1) AS ExpectedCount,
       agg.ActualCount,
       agg.ExpectedIds,
       agg.ActualIds,
       CAST(CASE WHEN agg.ExpectedIds = agg.ActualIds THEN 1 ELSE 0 END AS BIT) AS Passed
FROM agg
LEFT JOIN ctl.Controls c ON c.ControlId = agg.ControlNumber
ORDER BY agg.ControlNumber;

SELECT SUM(CASE WHEN e.ExpectedIds = ISNULL(x.ActualIds, N'') THEN 1 ELSE 0 END) AS PassedControls,
       COUNT(*) AS TotalControls
FROM @expected e
OUTER APPLY (SELECT STRING_AGG(CAST(a.Id AS NVARCHAR(20)), N',') WITHIN GROUP (ORDER BY a.Id) AS ActualIds
             FROM @actual a WHERE a.ControlNumber = e.ControlNumber) x;
GO

/* ---------------- security checks: controls_runner ---------------- */
DECLARE @sec TABLE (CheckName NVARCHAR(100), Blocked BIT, ErrorMessage NVARCHAR(400));

-- (a) SELECT on ops must SUCCEED
BEGIN TRY
    EXECUTE AS USER = 'controls_runner';
    DECLARE @n INT = (SELECT COUNT(*) FROM ops.Deliveries);
    REVERT;
    INSERT INTO @sec VALUES (N'SELECT on ops (should be allowed)', 0, NULL);
END TRY
BEGIN CATCH
    IF USER_NAME() = N'controls_runner' REVERT;
    INSERT INTO @sec VALUES (N'SELECT on ops (should be allowed)', 1, ERROR_MESSAGE());
END CATCH;

-- (b) UPDATE on ops must FAIL
BEGIN TRY
    EXECUTE AS USER = 'controls_runner';
    UPDATE ops.Deliveries SET PackageWeightKg = PackageWeightKg WHERE 1 = 0;
    REVERT;
    INSERT INTO @sec VALUES (N'UPDATE on ops (should be blocked)', 0, NULL);
END TRY
BEGIN CATCH
    IF USER_NAME() = N'controls_runner' REVERT;
    INSERT INTO @sec VALUES (N'UPDATE on ops (should be blocked)', 1, ERROR_MESSAGE());
END CATCH;

-- (c) SELECT on ctl must FAIL
BEGIN TRY
    EXECUTE AS USER = 'controls_runner';
    DECLARE @m INT = (SELECT COUNT(*) FROM ctl.Controls);
    REVERT;
    INSERT INTO @sec VALUES (N'SELECT on ctl (should be blocked)', 0, NULL);
END TRY
BEGIN CATCH
    IF USER_NAME() = N'controls_runner' REVERT;
    INSERT INTO @sec VALUES (N'SELECT on ctl (should be blocked)', 1, ERROR_MESSAGE());
END CATCH;

SELECT CheckName,
       Blocked,
       CAST(CASE WHEN (CheckName LIKE N'%allowed%' AND Blocked = 0)
                   OR (CheckName LIKE N'%blocked%' AND Blocked = 1) THEN 1 ELSE 0 END AS BIT) AS Passed,
       ErrorMessage
FROM @sec;
GO
