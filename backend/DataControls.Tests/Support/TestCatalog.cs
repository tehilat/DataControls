using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Metadata;

namespace DataControls.Tests.Support;

/// <summary>
/// In-memory catalog mirroring database/03_metadata.sql plus the physical columns of 01_schema.sql.
/// Built through <see cref="MetadataCatalogFactory"/>, i.e. the same validation path as production.
/// (Physical names are allowed here: this is test data, not engine code.)
/// </summary>
public static class TestCatalog
{
    public static MetadataCatalog Create() => MetadataCatalogFactory.Create(Rows(), ["ops"], out _);

    public static MetadataRows Rows(
        IEnumerable<EntityRow>? extraEntities = null,
        IEnumerable<FieldRow>? extraFields = null,
        IEnumerable<RelationRow>? extraRelations = null,
        IEnumerable<PhysicalColumnRow>? extraColumns = null)
    {
        var fieldId = 0;
        var fields = new List<FieldRow>();
        void F(string entity, string key, string display, string type, string? unit = null, bool show = true) =>
            fields.Add(new FieldRow
            {
                FieldId = ++fieldId, EntityKey = entity, FieldKey = key, DisplayName = display, ColumnName = key,
                DataType = type, Unit = unit, ShowInResults = show, SortOrder = fieldId * 10
            });

        F("Delivery", "DeliveryId", "מספר משלוח", "number");
        F("Delivery", "PackageWeightKg", "משקל חבילה", "number", "ק\"ג");
        F("Delivery", "DistanceKm", "מרחק משלוח", "number", "ק\"מ");
        F("Delivery", "DispatchedAt", "זמן יציאה", "datetime");
        F("Delivery", "DeliveredAt", "זמן מסירה", "datetime");
        F("Delivery", "Status", "סטטוס משלוח", "enum");                 // FieldId 6
        F("Drone", "DroneId", "מספר רחפן", "number", show: false);
        F("Drone", "SerialNumber", "מספר סידורי", "text");
        F("Drone", "Model", "דגם", "text");
        F("Drone", "MaxPayloadKg", "כושר נשיאה", "number", "ק\"ג");
        F("Drone", "MaxRangeKm", "טווח טיסה", "number", "ק\"מ");
        F("Drone", "LastMaintenance", "תחזוקה אחרונה", "date");
        F("Drone", "NextMaintenanceDue", "מועד תחזוקה הבא", "date");
        F("Drone", "Status", "סטטוס רחפן", "enum");                     // FieldId 14
        F("Hub", "HubId", "מספר מוקד", "number", show: false);
        F("Hub", "Name", "שם מוקד", "text");
        F("Hub", "City", "עיר מוקד", "text");
        F("Hub", "IsActive", "מוקד פעיל", "boolean");
        F("Pilot", "PilotId", "מספר מפעיל", "number", show: false);
        F("Pilot", "FullName", "שם מפעיל", "text");
        F("Pilot", "LicenseNumber", "מספר רישיון", "text");
        F("Pilot", "LicenseExpiry", "תוקף רישיון", "date");
        F("Pilot", "IsActive", "מפעיל פעיל", "boolean");
        F("Customer", "CustomerId", "מספר לקוח", "number", show: false);
        F("Customer", "FullName", "שם לקוח", "text");
        F("Customer", "Phone", "טלפון", "text");
        F("Customer", "Email", "אימייל", "text");
        F("Customer", "City", "עיר", "text");
        F("Customer", "Address", "כתובת", "text");

        var columns = new List<PhysicalColumnRow>();
        void C(string table, string column, string type, byte precision = 0, byte scale = 0, short maxLength = 0) =>
            columns.Add(new PhysicalColumnRow
            {
                SchemaName = "ops", TableName = table, ColumnName = column, SqlType = type,
                Precision = precision, Scale = scale, MaxLength = maxLength, IsNullable = true
            });

        C("Deliveries", "DeliveryId", "int", 10); C("Deliveries", "DroneId", "int", 10); C("Deliveries", "PilotId", "int", 10);
        C("Deliveries", "CustomerId", "int", 10); C("Deliveries", "PackageWeightKg", "decimal", 6, 2); C("Deliveries", "DistanceKm", "decimal", 6, 2);
        C("Deliveries", "DispatchedAt", "datetime2"); C("Deliveries", "DeliveredAt", "datetime2"); C("Deliveries", "Status", "nvarchar", maxLength: 40);
        C("Drones", "DroneId", "int", 10); C("Drones", "SerialNumber", "nvarchar", maxLength: 100); C("Drones", "HubId", "int", 10);
        C("Drones", "Model", "nvarchar", maxLength: 200); C("Drones", "MaxPayloadKg", "decimal", 6, 2); C("Drones", "MaxRangeKm", "decimal", 6, 2);
        C("Drones", "LastMaintenance", "date"); C("Drones", "NextMaintenanceDue", "date"); C("Drones", "Status", "nvarchar", maxLength: 40);
        C("Hubs", "HubId", "int", 10); C("Hubs", "Name", "nvarchar", maxLength: 200); C("Hubs", "City", "nvarchar", maxLength: 200); C("Hubs", "IsActive", "bit");
        C("Pilots", "PilotId", "int", 10); C("Pilots", "FullName", "nvarchar", maxLength: 200); C("Pilots", "LicenseNumber", "nvarchar", maxLength: 100);
        C("Pilots", "LicenseExpiry", "date"); C("Pilots", "IsActive", "bit");
        foreach (var c in new[] { "FullName", "Phone", "Email", "City", "Address" }) C("Customers", c, "nvarchar", maxLength: 200);
        C("Customers", "CustomerId", "int", 10);

        var entities = new List<EntityRow>
        {
            E("Delivery", "משלוח", "משלוחים", "Deliveries", "DeliveryId", "DeliveryId"),
            E("Drone", "רחפן", "רחפנים", "Drones", "DroneId", "SerialNumber"),
            E("Hub", "מוקד", "מוקדים", "Hubs", "HubId", "Name"),
            E("Pilot", "מפעיל", "מפעילים", "Pilots", "PilotId", "FullName"),
            E("Customer", "לקוח", "לקוחות", "Customers", "CustomerId", "FullName")
        };

        var relations = new List<RelationRow>
        {
            R("Delivery", "Drone", "Drone", "DroneId", "DroneId"),
            R("Delivery", "Pilot", "Pilot", "PilotId", "PilotId"),
            R("Delivery", "Customer", "Customer", "CustomerId", "CustomerId"),
            R("Drone", "Hub", "Hub", "HubId", "HubId")
        };

        var options = new List<FieldOptionRow>
        {
            O(6, "Pending", "ממתין", 1), O(6, "InTransit", "בדרך", 2), O(6, "Delivered", "נמסר", 3), O(6, "Failed", "נכשל", 4),
            O(14, "Active", "בשירות", 1), O(14, "Maintenance", "בתחזוקה", 2), O(14, "Retired", "הוצא משירות", 3)
        };

        var operators = new List<OperatorRow>
        {
            Op("eq", "שווה ל", true, 1), Op("neq", "שונה מ", true, 2), Op("gt", "גדול מ", true, 3),
            Op("gte", "גדול או שווה ל", true, 4), Op("lt", "קטן מ", true, 5), Op("lte", "קטן או שווה ל", true, 6),
            Op("contains", "מכיל", true, 7), Op("isEmpty", "ריק", false, 8), Op("isNotEmpty", "לא ריק", false, 9)
        };

        var dto = new List<DataTypeOperatorRow>();
        void D(string type, string op, string? display, bool fieldCompare) =>
            dto.Add(new DataTypeOperatorRow { DataType = type, OperatorKey = op, DisplayNameOverride = display, AllowFieldCompare = fieldCompare });

        foreach (var op in new[] { "eq", "neq", "gt", "gte", "lt", "lte" }) D("number", op, null, true);
        D("number", "isEmpty", null, false); D("number", "isNotEmpty", null, false);
        D("text", "eq", null, true); D("text", "neq", null, true); D("text", "contains", null, false);
        D("text", "isEmpty", null, false); D("text", "isNotEmpty", null, false);
        foreach (var type in new[] { "date", "datetime" })
        {
            D(type, "eq", type == "date" ? "בתאריך" : "בדיוק ב", true);
            D(type, "neq", type == "date" ? "לא בתאריך" : "לא בדיוק ב", true);
            D(type, "gt", "אחרי", true); D(type, "gte", "באותו מועד או אחרי", true);
            D(type, "lt", "לפני", true); D(type, "lte", "באותו מועד או לפני", true);
            D(type, "isEmpty", null, false); D(type, "isNotEmpty", null, false);
        }
        D("boolean", "eq", "הוא", false); D("boolean", "neq", "אינו", false);
        D("enum", "eq", "הוא", false); D("enum", "neq", "אינו", false);

        return new MetadataRows
        {
            Entities = [.. entities, .. extraEntities ?? []],
            Fields = [.. fields, .. extraFields ?? []],
            Options = options,
            Relations = [.. relations, .. extraRelations ?? []],
            Operators = operators,
            DataTypeOperators = dto,
            Columns = [.. columns, .. extraColumns ?? []]
        };
    }

    public static EntityRow E(string key, string display, string plural, string table, string pk, string displayColumn,
        string schema = "ops") =>
        new()
        {
            EntityKey = key, DisplayName = display, DisplayNamePlural = plural, SchemaName = schema,
            TableName = table, PrimaryKeyColumn = pk, DisplayColumn = displayColumn
        };

    public static RelationRow R(string from, string key, string to, string fromColumn, string toColumn) =>
        new() { FromEntityKey = from, RelationKey = key, ToEntityKey = to, FromColumn = fromColumn, ToColumn = toColumn, DisplayName = key };

    private static FieldOptionRow O(int fieldId, string value, string display, int order) =>
        new() { FieldId = fieldId, OptionValue = value, DisplayName = display, SortOrder = order };

    private static OperatorRow Op(string key, string display, bool requiresValue, int order) =>
        new() { OperatorKey = key, DisplayName = display, RequiresValue = requiresValue, SortOrder = order };
}
