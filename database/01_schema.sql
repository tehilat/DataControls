/* =====================================================================
   01_schema.sql — DroneOpsControls: database, schemas and tables.
   Drops and recreates the database, so it is safe to re-run.

   Schemas:
     ops  — business data (drone delivery fleet)
     meta — business catalog (maps Hebrew business terms to tables/columns)
     ctl  — control definitions (JSON) and run history

   Note: ops tables deliberately have only PK / FK / status CHECK constraints.
   No constraints that would block bad data (negative weight, reversed dates),
   because the controls are supposed to find that data.
   ===================================================================== */
SET NOCOUNT ON;
USE master;
GO

IF DB_ID(N'DroneOpsControls') IS NOT NULL
BEGIN
    ALTER DATABASE DroneOpsControls SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE DroneOpsControls;
END
GO

CREATE DATABASE DroneOpsControls;
GO

USE DroneOpsControls;
GO

CREATE SCHEMA ops;
GO
CREATE SCHEMA meta;
GO
CREATE SCHEMA ctl;
GO

/* ---------------------------------------------------------------------
   ops — business data
   --------------------------------------------------------------------- */
CREATE TABLE ops.Hubs
(
    HubId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Hubs PRIMARY KEY,
    Name      NVARCHAR(100)     NOT NULL,
    City      NVARCHAR(100)     NOT NULL,
    IsActive  BIT               NOT NULL CONSTRAINT DF_Hubs_IsActive DEFAULT (1)
);

CREATE TABLE ops.Drones
(
    DroneId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Drones PRIMARY KEY,
    SerialNumber       NVARCHAR(50)      NOT NULL CONSTRAINT UQ_Drones_SerialNumber UNIQUE,
    HubId              INT               NOT NULL CONSTRAINT FK_Drones_Hubs REFERENCES ops.Hubs(HubId),
    Model              NVARCHAR(100)     NOT NULL,
    MaxPayloadKg       DECIMAL(6,2)      NOT NULL,
    MaxRangeKm         DECIMAL(6,2)      NOT NULL,
    LastMaintenance    DATE              NULL,
    NextMaintenanceDue DATE              NULL,
    Status             NVARCHAR(20)      NOT NULL
        CONSTRAINT CK_Drones_Status CHECK (Status IN (N'Active', N'Maintenance', N'Retired'))
);
CREATE INDEX IX_Drones_HubId ON ops.Drones(HubId);

CREATE TABLE ops.Pilots
(
    PilotId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pilots PRIMARY KEY,
    FullName      NVARCHAR(100)     NOT NULL,
    LicenseNumber NVARCHAR(50)      NULL,
    LicenseExpiry DATE              NULL,
    IsActive      BIT               NOT NULL CONSTRAINT DF_Pilots_IsActive DEFAULT (1)
);

CREATE TABLE ops.Customers
(
    CustomerId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    FullName   NVARCHAR(100)     NOT NULL,
    Phone      NVARCHAR(30)      NULL,
    Email      NVARCHAR(200)     NULL,
    City       NVARCHAR(100)     NULL,
    Address    NVARCHAR(200)     NULL
);

CREATE TABLE ops.Deliveries
(
    DeliveryId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Deliveries PRIMARY KEY,
    DroneId         INT               NOT NULL CONSTRAINT FK_Deliveries_Drones    REFERENCES ops.Drones(DroneId),
    PilotId         INT               NOT NULL CONSTRAINT FK_Deliveries_Pilots    REFERENCES ops.Pilots(PilotId),
    CustomerId      INT               NOT NULL CONSTRAINT FK_Deliveries_Customers REFERENCES ops.Customers(CustomerId),
    PackageWeightKg DECIMAL(6,2)      NOT NULL,
    DistanceKm      DECIMAL(6,2)      NOT NULL,
    DispatchedAt    DATETIME2(0)      NULL,
    DeliveredAt     DATETIME2(0)      NULL,
    Status          NVARCHAR(20)      NOT NULL
        CONSTRAINT CK_Deliveries_Status CHECK (Status IN (N'Pending', N'InTransit', N'Delivered', N'Failed'))
);
CREATE INDEX IX_Deliveries_DroneId    ON ops.Deliveries(DroneId);
CREATE INDEX IX_Deliveries_PilotId    ON ops.Deliveries(PilotId);
CREATE INDEX IX_Deliveries_CustomerId ON ops.Deliveries(CustomerId);
GO

/* ---------------------------------------------------------------------
   meta — business catalog. No SQL is stored here, only identifiers
   that the query builder uses as a whitelist.
   --------------------------------------------------------------------- */
CREATE TABLE meta.Entities
(
    EntityKey         NVARCHAR(50)  NOT NULL CONSTRAINT PK_Entities PRIMARY KEY,
    DisplayName       NVARCHAR(100) NOT NULL,
    DisplayNamePlural NVARCHAR(100) NOT NULL,
    Description       NVARCHAR(500) NULL,
    SchemaName        SYSNAME       NOT NULL,
    TableName         SYSNAME       NOT NULL,
    PrimaryKeyColumn  SYSNAME       NOT NULL,
    DisplayColumn     SYSNAME       NOT NULL,
    IsActive          BIT           NOT NULL CONSTRAINT DF_Entities_IsActive DEFAULT (1)
);

CREATE TABLE meta.Fields
(
    FieldId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Fields PRIMARY KEY,
    EntityKey    NVARCHAR(50)  NOT NULL CONSTRAINT FK_Fields_Entities REFERENCES meta.Entities(EntityKey),
    FieldKey     NVARCHAR(50)  NOT NULL,
    DisplayName  NVARCHAR(100) NOT NULL,
    Description  NVARCHAR(500) NULL,
    ColumnName   SYSNAME       NOT NULL,
    DataType     NVARCHAR(20)  NOT NULL
        CONSTRAINT CK_Fields_DataType CHECK (DataType IN (N'number', N'text', N'date', N'datetime', N'boolean', N'enum')),
    Unit         NVARCHAR(20)  NULL,
    ShowInResults BIT          NOT NULL CONSTRAINT DF_Fields_ShowInResults DEFAULT (1),
    SortOrder    INT           NOT NULL CONSTRAINT DF_Fields_SortOrder DEFAULT (0),
    IsActive     BIT           NOT NULL CONSTRAINT DF_Fields_IsActive DEFAULT (1),
    CONSTRAINT UQ_Fields_EntityKey_FieldKey UNIQUE (EntityKey, FieldKey)
);
CREATE INDEX IX_Fields_EntityKey ON meta.Fields(EntityKey);

CREATE TABLE meta.FieldOptions
(
    FieldOptionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FieldOptions PRIMARY KEY,
    FieldId       INT           NOT NULL CONSTRAINT FK_FieldOptions_Fields REFERENCES meta.Fields(FieldId),
    OptionValue   NVARCHAR(50)  NOT NULL,
    DisplayName   NVARCHAR(100) NOT NULL,
    SortOrder     INT           NOT NULL CONSTRAINT DF_FieldOptions_SortOrder DEFAULT (0),
    CONSTRAINT UQ_FieldOptions_FieldId_OptionValue UNIQUE (FieldId, OptionValue)
);
CREATE INDEX IX_FieldOptions_FieldId ON meta.FieldOptions(FieldId);

CREATE TABLE meta.Relations
(
    RelationId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Relations PRIMARY KEY,
    FromEntityKey NVARCHAR(50)  NOT NULL CONSTRAINT FK_Relations_FromEntity REFERENCES meta.Entities(EntityKey),
    RelationKey   NVARCHAR(50)  NOT NULL,
    ToEntityKey   NVARCHAR(50)  NOT NULL CONSTRAINT FK_Relations_ToEntity REFERENCES meta.Entities(EntityKey),
    FromColumn    SYSNAME       NOT NULL,
    ToColumn      SYSNAME       NOT NULL,
    DisplayName   NVARCHAR(100) NOT NULL,
    CONSTRAINT UQ_Relations_From_Key UNIQUE (FromEntityKey, RelationKey)
);
CREATE INDEX IX_Relations_FromEntityKey ON meta.Relations(FromEntityKey);
CREATE INDEX IX_Relations_ToEntityKey   ON meta.Relations(ToEntityKey);

CREATE TABLE meta.Operators
(
    OperatorKey   NVARCHAR(20)  NOT NULL CONSTRAINT PK_Operators PRIMARY KEY,
    DisplayName   NVARCHAR(100) NOT NULL,
    RequiresValue BIT           NOT NULL,
    SortOrder     INT           NOT NULL
);

CREATE TABLE meta.DataTypeOperators
(
    DataType            NVARCHAR(20)  NOT NULL
        CONSTRAINT CK_DataTypeOperators_DataType CHECK (DataType IN (N'number', N'text', N'date', N'datetime', N'boolean', N'enum')),
    OperatorKey         NVARCHAR(20)  NOT NULL CONSTRAINT FK_DataTypeOperators_Operators REFERENCES meta.Operators(OperatorKey),
    DisplayNameOverride NVARCHAR(100) NULL,
    AllowFieldCompare   BIT           NOT NULL CONSTRAINT DF_DataTypeOperators_AllowFieldCompare DEFAULT (0),
    CONSTRAINT PK_DataTypeOperators PRIMARY KEY (DataType, OperatorKey)
);
CREATE INDEX IX_DataTypeOperators_OperatorKey ON meta.DataTypeOperators(OperatorKey);
GO

/* ---------------------------------------------------------------------
   ctl — controls and runs
   --------------------------------------------------------------------- */
CREATE TABLE ctl.Controls
(
    ControlId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Controls PRIMARY KEY,
    Name           NVARCHAR(200)  NOT NULL CONSTRAINT UQ_Controls_Name UNIQUE,
    Description    NVARCHAR(1000) NULL,
    RootEntityKey  NVARCHAR(50)   NOT NULL CONSTRAINT FK_Controls_Entities REFERENCES meta.Entities(EntityKey),
    DefinitionJson NVARCHAR(MAX)  NOT NULL CONSTRAINT CK_Controls_DefinitionJson CHECK (ISJSON(DefinitionJson) = 1),
    Severity       NVARCHAR(10)   NOT NULL CONSTRAINT CK_Controls_Severity CHECK (Severity IN (N'Low', N'Medium', N'High')),
    Source         NVARCHAR(10)   NOT NULL CONSTRAINT CK_Controls_Source CHECK (Source IN (N'Manual', N'AI'))
                                           CONSTRAINT DF_Controls_Source DEFAULT (N'Manual'),
    IsActive       BIT            NOT NULL CONSTRAINT DF_Controls_IsActive DEFAULT (1),
    CreatedAt      DATETIME2(0)   NOT NULL CONSTRAINT DF_Controls_CreatedAt DEFAULT (SYSDATETIME()),
    UpdatedAt      DATETIME2(0)   NOT NULL CONSTRAINT DF_Controls_UpdatedAt DEFAULT (SYSDATETIME()),
    RowVer         ROWVERSION     NOT NULL
);
CREATE INDEX IX_Controls_RootEntityKey ON ctl.Controls(RootEntityKey);

CREATE TABLE ctl.ControlRuns
(
    RunId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ControlRuns PRIMARY KEY,
    ControlId      INT            NOT NULL CONSTRAINT FK_ControlRuns_Controls REFERENCES ctl.Controls(ControlId),
    StartedAt      DATETIME2(3)   NOT NULL CONSTRAINT DF_ControlRuns_StartedAt DEFAULT (SYSDATETIME()),
    FinishedAt     DATETIME2(3)   NULL,
    Status         NVARCHAR(20)   NOT NULL CONSTRAINT CK_ControlRuns_Status CHECK (Status IN (N'Running', N'Succeeded', N'Failed')),
    ViolationCount INT            NULL,
    DurationMs     INT            NULL,
    ErrorMessage   NVARCHAR(2000) NULL,
    GeneratedSql   NVARCHAR(MAX)  NULL,
    -- snapshot of the result columns (business key, Hebrew name, data type, unit) at run time
    ColumnsJson    NVARCHAR(MAX)  NULL CONSTRAINT CK_ControlRuns_ColumnsJson CHECK (ColumnsJson IS NULL OR ISJSON(ColumnsJson) = 1)
);
CREATE INDEX IX_ControlRuns_ControlId ON ctl.ControlRuns(ControlId);

/* The failing records of a run. Values are stored in a canonical format
   (numbers invariant, dates ISO 8601) so the API can sort and filter them by type. */
CREATE TABLE ctl.ControlRunViolations
(
    RunId        INT            NOT NULL CONSTRAINT FK_ControlRunViolations_ControlRuns
                                         REFERENCES ctl.ControlRuns(RunId) ON DELETE CASCADE,
    RowNumber    INT            NOT NULL,
    RecordId     NVARCHAR(100)  NULL,
    DisplayValue NVARCHAR(400)  NULL,
    ValuesJson   NVARCHAR(MAX)  NOT NULL CONSTRAINT CK_ControlRunViolations_ValuesJson CHECK (ISJSON(ValuesJson) = 1),
    Reason       NVARCHAR(4000) NOT NULL,
    SearchText   NVARCHAR(MAX)  NOT NULL,
    CONSTRAINT PK_ControlRunViolations PRIMARY KEY (RunId, RowNumber)
);
GO

PRINT N'01_schema.sql completed.';
