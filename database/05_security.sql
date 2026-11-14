/* =====================================================================
   05_security.sql — least-privilege database users.

   The server uses Windows authentication only, so there are no SQL logins
   or passwords. Both users are created WITHOUT LOGIN:

     controls_app    — the application's permission set:
                       SELECT on meta, CRUD on ctl, IMPERSONATE on controls_runner.
     controls_runner — used only while executing a generated control query:
                       SELECT on schema ops, DENY on everything else.

   The engine runs each control inside
       EXECUTE AS USER = 'controls_runner';  <generated SELECT>;  REVERT;
   so even a faulty generated query can only read ops data.

   Note: in local development the app connects with the developer's Windows
   login (dbo / sysadmin), which can impersonate implicitly. In a deployed
   environment, map the app's Windows login to a user and add it to the
   permissions given to controls_app (or EXECUTE AS controls_app).

   Re-runnable.
   ===================================================================== */
SET NOCOUNT ON;
USE DroneOpsControls;
GO

IF USER_ID(N'controls_app') IS NOT NULL    DROP USER controls_app;
IF USER_ID(N'controls_runner') IS NOT NULL DROP USER controls_runner;
GO

CREATE USER controls_app    WITHOUT LOGIN WITH DEFAULT_SCHEMA = ctl;
CREATE USER controls_runner WITHOUT LOGIN WITH DEFAULT_SCHEMA = ops;
GO

/* ---------------- controls_app ---------------- */
GRANT SELECT ON SCHEMA::meta TO controls_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::ctl TO controls_app;
GRANT IMPERSONATE ON USER::controls_runner TO controls_app;
DENY  INSERT, UPDATE, DELETE ON SCHEMA::ops  TO controls_app;
DENY  INSERT, UPDATE, DELETE ON SCHEMA::meta TO controls_app;
GO

/* ---------------- controls_runner ---------------- */
GRANT SELECT ON SCHEMA::ops TO controls_runner;   -- schema level: covers tables added later (e.g. 06)
DENY  INSERT, UPDATE, DELETE, ALTER, EXECUTE, REFERENCES ON SCHEMA::ops TO controls_runner;
DENY  SELECT, INSERT, UPDATE, DELETE, ALTER, EXECUTE, REFERENCES ON SCHEMA::meta TO controls_runner;
DENY  SELECT, INSERT, UPDATE, DELETE, ALTER, EXECUTE, REFERENCES ON SCHEMA::ctl  TO controls_runner;
DENY  SELECT, INSERT, UPDATE, DELETE, ALTER, EXECUTE, REFERENCES ON SCHEMA::dbo  TO controls_runner;
DENY  CREATE TABLE, CREATE VIEW, CREATE PROCEDURE, CREATE FUNCTION TO controls_runner;
GO

PRINT N'05_security.sql completed.';
