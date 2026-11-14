using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Sql;

namespace DataControls.Infrastructure.Metadata;

/// <summary>
/// Turns raw meta rows into a <see cref="MetadataCatalog"/>, dropping anything unsafe or inconsistent:
/// identifiers that are not plain identifiers, schemas outside the allowed list, and columns/tables
/// that do not physically exist. So the whitelist is checked against both meta and the real schema.
/// </summary>
public static class MetadataCatalogFactory
{
    public static MetadataCatalog Create(MetadataRows rows, IReadOnlyCollection<string> allowedSchemas, out IReadOnlyList<string> warnings)
    {
        var problems = new List<string>();
        var schemas = new HashSet<string>(allowedSchemas, StringComparer.OrdinalIgnoreCase);
        var columns = rows.Columns.ToDictionary(
            c => ColumnKey(c.SchemaName, c.TableName, c.ColumnName),
            ToColumnInfo,
            StringComparer.OrdinalIgnoreCase);

        bool TryColumn(EntityRow entity, string column, out ColumnInfo info) =>
            columns.TryGetValue(ColumnKey(entity.SchemaName, entity.TableName, column), out info!);

        // entities
        var entities = new Dictionary<string, EntityRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in rows.Entities)
        {
            if (!schemas.Contains(e.SchemaName))
                problems.Add($"Entity '{e.EntityKey}': schema '{e.SchemaName}' is not allowed.");
            else if (!AllValid(e.SchemaName, e.TableName, e.PrimaryKeyColumn, e.DisplayColumn))
                problems.Add($"Entity '{e.EntityKey}': unsafe identifier.");
            else if (!TryColumn(e, e.PrimaryKeyColumn, out _) || !TryColumn(e, e.DisplayColumn, out _))
                problems.Add($"Entity '{e.EntityKey}': table or key/display column does not exist.");
            else
                entities[e.EntityKey] = e;
        }

        // fields
        var fields = new List<FieldMeta>();
        foreach (var f in rows.Fields)
        {
            if (!entities.TryGetValue(f.EntityKey, out var entity)) continue;
            if (!SqlIdentifier.IsValid(f.ColumnName))
                problems.Add($"Field '{f.EntityKey}.{f.FieldKey}': unsafe column name.");
            else if (!FieldDataTypes.TryParse(f.DataType, out var dataType))
                problems.Add($"Field '{f.EntityKey}.{f.FieldKey}': unknown data type '{f.DataType}'.");
            else if (!TryColumn(entity, f.ColumnName, out var column))
                problems.Add($"Field '{f.EntityKey}.{f.FieldKey}': column '{f.ColumnName}' does not exist.");
            else
                fields.Add(new FieldMeta(f.FieldId, entity.EntityKey, f.FieldKey, f.DisplayName, f.Description,
                    f.ColumnName, dataType, f.Unit, f.ShowInResults, f.SortOrder, column));
        }

        // relations
        var relations = new List<RelationMeta>();
        foreach (var r in rows.Relations)
        {
            if (!entities.TryGetValue(r.FromEntityKey, out var from) || !entities.TryGetValue(r.ToEntityKey, out var to))
                continue;
            if (!AllValid(r.FromColumn, r.ToColumn))
                problems.Add($"Relation '{r.FromEntityKey}.{r.RelationKey}': unsafe column name.");
            else if (!TryColumn(from, r.FromColumn, out _) || !TryColumn(to, r.ToColumn, out _))
                problems.Add($"Relation '{r.FromEntityKey}.{r.RelationKey}': join column does not exist.");
            else
                relations.Add(new RelationMeta(from.EntityKey, r.RelationKey, to.EntityKey, r.FromColumn, r.ToColumn, r.DisplayName));
        }

        // data type ↔ operator rules
        var dataTypeOperators = new List<DataTypeOperatorMeta>();
        foreach (var d in rows.DataTypeOperators)
        {
            if (FieldDataTypes.TryParse(d.DataType, out var dataType))
                dataTypeOperators.Add(new DataTypeOperatorMeta(dataType, d.OperatorKey, d.DisplayNameOverride, d.AllowFieldCompare));
            else
                problems.Add($"DataTypeOperator: unknown data type '{d.DataType}'.");
        }

        warnings = problems;
        return new MetadataCatalog(
            entities.Values.Select(e => new EntityMeta(e.EntityKey, e.DisplayName, e.DisplayNamePlural, e.Description,
                e.SchemaName, e.TableName, e.PrimaryKeyColumn, e.DisplayColumn)),
            fields,
            rows.Options.Select(o => new FieldOptionMeta(o.FieldId, o.OptionValue, o.DisplayName, o.SortOrder)),
            relations,
            rows.Operators.Select(o => new OperatorMeta(o.OperatorKey, o.DisplayName, o.RequiresValue, o.SortOrder)),
            dataTypeOperators);
    }

    private static bool AllValid(params string[] identifiers) => identifiers.All(SqlIdentifier.IsValid);

    private static string ColumnKey(string schema, string table, string column) => $"{schema}\u001F{table}\u001F{column}";

    private static ColumnInfo ToColumnInfo(PhysicalColumnRow c)
    {
        var type = c.SqlType.ToLowerInvariant();
        var maxLength = c.MaxLength == -1 ? -1
            : type is "nvarchar" or "nchar" ? c.MaxLength / 2
            : c.MaxLength;
        return new ColumnInfo(type, maxLength, c.Precision, c.Scale, c.IsNullable);
    }
}
