using System.Globalization;
using System.Text.Json;
using DataControls.Application.Errors;
using DataControls.Application.Formatting;
using DataControls.Domain.Definition;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Binding;

/// <summary>Hard limits that keep a definition (manual or AI-generated) small and safe.</summary>
public static class DefinitionLimits
{
    public const int MaxDepth = 10;
    public const int MaxRules = 100;
    public const int MaxPathSegments = 5;
    public const int MaxTextLength = 4000;
    public const int MaxOffsetDays = 36_500;
}

/// <summary>
/// Validates a <see cref="ControlDefinition"/> against the metadata catalog and resolves it into a
/// <see cref="BoundDefinition"/>. All problems are collected and reported together, in Hebrew.
/// Nothing here falls back to physical names: unknown keys are errors.
/// </summary>
public sealed class DefinitionBinder
{
    /// <exception cref="ControlValidationException">The definition is invalid.</exception>
    public BoundDefinition Bind(ControlDefinition definition, MetadataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(catalog);

        var ctx = new Context(catalog);

        if (definition.Version != ControlDefinition.CurrentVersion)
            ctx.Error("version",
                $"גרסת הגדרה {definition.Version} אינה נתמכת. הגרסה הנתמכת היא {ControlDefinition.CurrentVersion}.");

        if (!catalog.TryGetEntity(definition.RootEntity, out var root))
        {
            ctx.Error("rootEntity", $"הישות \"{Clip(definition.RootEntity)}\" אינה קיימת בקטלוג.");
            throw new ControlValidationException(ctx.Errors);
        }
        ctx.Root = root;

        BoundNode? condition = null;
        if (definition.Condition is null)
            ctx.Error("condition", "לא הוגדר תנאי לבקרה.");
        else
            condition = BindNode(definition.Condition, "condition", 1, ctx);

        if (ctx.Errors.Count > 0 || condition is null)
            throw new ControlValidationException(ctx.Errors);

        var resultFields = catalog.GetFields(root.EntityKey).Where(f => f.ShowInResults).ToList();
        return new BoundDefinition(root, condition, ctx.Joins, ctx.Rules, resultFields);
    }

    private static BoundNode? BindNode(ConditionNode node, string path, int depth, Context ctx) => node switch
    {
        ConditionGroup group => BindGroup(group, path, depth, ctx),
        ConditionRule rule => BindRule(rule, path, ctx),
        _ => ctx.ErrorNull<BoundNode>(path, "סוג צומת לא מוכר.")
    };

    private static BoundGroup? BindGroup(ConditionGroup group, string path, int depth, Context ctx)
    {
        if (depth > DefinitionLimits.MaxDepth)
            return ctx.ErrorNull<BoundGroup>(path, $"קינון הקבוצות עמוק מדי (מותר עד {DefinitionLimits.MaxDepth} רמות).");

        if (!Enum.IsDefined(group.Logic))
            ctx.Error($"{path}.logic", "חיבור בין תנאים חייב להיות AND או OR.");

        if (group.Children.Count == 0)
            return ctx.ErrorNull<BoundGroup>(path, "קבוצת תנאים ריקה. יש להוסיף לפחות תנאי אחד.");

        var children = new List<BoundNode>();
        for (var i = 0; i < group.Children.Count; i++)
        {
            var childPath = $"{path}.children[{i}]";
            var child = group.Children[i];
            if (child is null)
            {
                ctx.Error(childPath, "תנאי ריק.");
                continue;
            }

            var bound = BindNode(child, childPath, depth + 1, ctx);
            if (bound is not null) children.Add(bound);
        }

        return children.Count == group.Children.Count ? new BoundGroup(group.Logic, children) : null;
    }

    private static BoundRule? BindRule(ConditionRule rule, string path, Context ctx)
    {
        if (++ctx.RuleCount > DefinitionLimits.MaxRules)
        {
            if (ctx.RuleCount == DefinitionLimits.MaxRules + 1)
                ctx.Error(path, $"יותר מדי תנאים בבקרה (מותר עד {DefinitionLimits.MaxRules}).");
            return null;
        }

        var field = ResolvePath(rule.Field, $"{path}.field", ctx);

        // operator: must exist in meta, be known to the engine, and be valid for the field's type
        if (!ctx.Catalog.TryGetOperator(rule.Operator, out var opMeta))
            return ctx.ErrorNull<BoundRule>($"{path}.operator", $"אופרטור לא מוכר: \"{Clip(rule.Operator)}\".");

        if (!ComparisonOperators.TryParse(opMeta.OperatorKey, out var op))
            return ctx.ErrorNull<BoundRule>($"{path}.operator", $"האופרטור \"{opMeta.DisplayName}\" אינו נתמך במנוע.");

        if (field is null) return null;

        if (!ctx.Catalog.TryGetDataTypeOperator(field.Field.DataType, opMeta.OperatorKey, out var typeRule))
            return ctx.ErrorNull<BoundRule>($"{path}.operator",
                $"האופרטור \"{opMeta.DisplayName}\" אינו מתאים לשדה \"{field.DisplayName}\" (סוג: {field.Field.DataType.HebrewName()}).");

        var opDisplay = typeRule.DisplayNameOverride ?? opMeta.DisplayName;

        // operand
        if (opMeta.RequiresValue && rule.CompareTo is null)
            return ctx.ErrorNull<BoundRule>($"{path}.compareTo", $"חסר ערך להשוואה בתנאי על \"{field.DisplayName}\".");

        if (!opMeta.RequiresValue && rule.CompareTo is not null)
            return ctx.ErrorNull<BoundRule>($"{path}.compareTo", $"האופרטור \"{opDisplay}\" אינו מקבל ערך להשוואה.");

        BoundOperand? operand = null;
        if (rule.CompareTo is not null)
        {
            operand = BindOperand(rule.CompareTo, field, typeRule, opDisplay, $"{path}.compareTo", ctx);
            if (operand is null) return null;
        }

        var bound = new BoundRule(ctx.Rules.Count, field, op, opDisplay, operand);
        ctx.Rules.Add(bound);
        return bound;
    }

    private static BoundOperand? BindOperand(Operand operand, BoundField left, DataTypeOperatorMeta typeRule,
        string opDisplay, string path, Context ctx)
    {
        switch (operand)
        {
            case ValueOperand v:
                return ConvertValue(v.Value, left, $"{path}.value", ctx);

            case FieldOperand f:
                if (!typeRule.AllowFieldCompare)
                    return ctx.ErrorNull<BoundOperand>(path,
                        $"לא ניתן להשוות את \"{left.DisplayName}\" לשדה אחר באופרטור \"{opDisplay}\".");
                var right = ResolvePath(f.Field, $"{path}.field", ctx);
                if (right is null) return null;
                if (!AreComparable(left.Field.DataType, right.Field.DataType))
                    return ctx.ErrorNull<BoundOperand>(path,
                        $"לא ניתן להשוות את \"{left.DisplayName}\" ({left.Field.DataType.HebrewName()}) " +
                        $"ל\"{right.DisplayName}\" ({right.Field.DataType.HebrewName()}).");
                return new BoundFieldOperand(right);

            case TodayOperand t:
                if (!left.Field.DataType.IsTemporal())
                    return ctx.ErrorNull<BoundOperand>(path, $"השוואה ל\"היום\" אפשרית רק בשדה תאריך, ו\"{left.DisplayName}\" אינו כזה.");
                if (Math.Abs((long)t.OffsetDays) > DefinitionLimits.MaxOffsetDays)
                    return ctx.ErrorNull<BoundOperand>($"{path}.offsetDays",
                        $"היסט של {t.OffsetDays} ימים חורג מהטווח המותר (בין {-DefinitionLimits.MaxOffsetDays} ל-{DefinitionLimits.MaxOffsetDays}).");
                return new BoundToday(t.OffsetDays);

            case NowOperand:
                if (!left.Field.DataType.IsTemporal())
                    return ctx.ErrorNull<BoundOperand>(path, $"השוואה ל\"עכשיו\" אפשרית רק בשדה תאריך, ו\"{left.DisplayName}\" אינו כזה.");
                return new BoundNow();

            default:
                return ctx.ErrorNull<BoundOperand>(path, "סוג ערך השוואה לא מוכר.");
        }
    }

    private static bool AreComparable(FieldDataType a, FieldDataType b) =>
        a == b || (a.IsTemporal() && b.IsTemporal());

    private static BoundField? ResolvePath(string? path, string jsonPath, Context ctx)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ctx.ErrorNull<BoundField>(jsonPath, "לא נבחר שדה.");

        var segments = path.Split('.');
        if (segments.Length > DefinitionLimits.MaxPathSegments || segments.Any(string.IsNullOrWhiteSpace))
            return ctx.ErrorNull<BoundField>(jsonPath, $"נתיב השדה \"{Clip(path)}\" אינו תקין.");

        var entity = ctx.Root;
        BoundJoin? join = null;
        var chainNames = new List<string>();

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!ctx.Catalog.TryGetRelation(entity.EntityKey, segments[i], out var relation)
                || !ctx.Catalog.TryGetEntity(relation.ToEntityKey, out var target))
                return ctx.ErrorNull<BoundField>(jsonPath,
                    $"הקשר \"{Clip(segments[i])}\" אינו קיים עבור הישות \"{entity.DisplayName}\".");

            join = ctx.GetOrAddJoin(join, relation, target);
            entity = target;
            chainNames.Add(target.DisplayName);
        }

        if (!ctx.Catalog.TryGetField(entity.EntityKey, segments[^1], out var field))
            return ctx.ErrorNull<BoundField>(jsonPath,
                $"השדה \"{Clip(segments[^1])}\" אינו קיים בישות \"{entity.DisplayName}\".");

        var canonicalPath = join is null ? field.FieldKey : $"{join.Path}.{field.FieldKey}";
        var display = chainNames.Count == 0
            ? field.DisplayName
            : $"{field.DisplayName} ({string.Join(" › ", chainNames)})";

        return new BoundField(canonicalPath, field, join, display);
    }

    private static BoundValue? ConvertValue(JsonElement value, BoundField left, string path, Context ctx)
    {
        var field = left.Field;
        var name = left.DisplayName;

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ctx.ErrorNull<BoundValue>(path, $"חסר ערך להשוואה בתנאי על \"{name}\". לבדיקת ערך חסר יש להשתמש באופרטור \"ריק\".");

        switch (field.DataType)
        {
            case FieldDataType.Number:
            {
                decimal number;
                if (value.ValueKind == JsonValueKind.Number)
                {
                    if (!value.TryGetDecimal(out number))
                        return ctx.ErrorNull<BoundValue>(path, $"הערך {Clip(value.GetRawText())} חורג מהטווח המותר לשדה \"{name}\".");
                }
                else if (value.ValueKind != JsonValueKind.String
                         || !decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                {
                    return ctx.ErrorNull<BoundValue>(path, $"השדה \"{name}\" הוא מספרי, ויש להזין בו מספר.");
                }

                var range = field.Column.NumericRange;
                if (range is not null && (number < range.Min || number > range.Max))
                    return ctx.ErrorNull<BoundValue>(path,
                        $"הערך {ValueFormatter.FormatNumber(number)} חורג מהטווח המותר לשדה \"{name}\" " +
                        $"(בין {ValueFormatter.FormatNumber(range.Min)} ל-{ValueFormatter.FormatNumber(range.Max)}).");

                return new BoundValue(number, ValueFormatter.WithUnit(ValueFormatter.FormatNumber(number), field.Unit));
            }

            case FieldDataType.Text:
            {
                if (value.ValueKind != JsonValueKind.String)
                    return ctx.ErrorNull<BoundValue>(path, $"השדה \"{name}\" הוא טקסט, ויש להזין בו טקסט.");
                var text = value.GetString()!;
                if (string.IsNullOrWhiteSpace(text))
                    return ctx.ErrorNull<BoundValue>(path, $"ערך ההשוואה לשדה \"{name}\" ריק. לבדיקת ערך חסר יש להשתמש באופרטור \"ריק\".");
                if (text.Length > DefinitionLimits.MaxTextLength)
                    return ctx.ErrorNull<BoundValue>(path, $"ערך ההשוואה לשדה \"{name}\" ארוך מדי (עד {DefinitionLimits.MaxTextLength} תווים).");
                return new BoundValue(text, $"\"{text}\"");
            }

            case FieldDataType.Date:
            {
                if (value.ValueKind != JsonValueKind.String || !TryParseDate(value.GetString()!, out var date))
                    return ctx.ErrorNull<BoundValue>(path, $"השדה \"{name}\" הוא תאריך. יש להזין תאריך בפורמט yyyy-MM-dd.");
                if (!InDateRange(date, field, path, name, ctx)) return null;
                return new BoundValue(date, date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            }

            case FieldDataType.DateTime:
            {
                if (value.ValueKind != JsonValueKind.String
                    || !DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                    return ctx.ErrorNull<BoundValue>(path, $"השדה \"{name}\" הוא תאריך ושעה. יש להזין ערך בפורמט yyyy-MM-ddTHH:mm.");
                dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
                if (!InDateRange(dateTime, field, path, name, ctx)) return null;
                return new BoundValue(dateTime, dateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
            }

            case FieldDataType.Boolean:
            {
                bool? flag = value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(value.GetString(), out var b) => b,
                    _ => null
                };
                if (flag is null)
                    return ctx.ErrorNull<BoundValue>(path, $"השדה \"{name}\" מקבל רק כן או לא.");
                return new BoundValue(flag.Value, flag.Value ? "כן" : "לא");
            }

            case FieldDataType.Enum:
            {
                var options = ctx.Catalog.GetOptions(field.FieldId);
                var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                var option = options.FirstOrDefault(o => string.Equals(o.OptionValue, raw, StringComparison.OrdinalIgnoreCase));
                if (option is null)
                    return ctx.ErrorNull<BoundValue>(path,
                        $"הערך \"{Clip(raw ?? value.GetRawText())}\" אינו ערך חוקי לשדה \"{name}\". " +
                        $"ערכים אפשריים: {string.Join(", ", options.Select(o => o.DisplayName))}.");
                return new BoundValue(option.OptionValue, option.DisplayName);
            }

            default:
                return ctx.ErrorNull<BoundValue>(path, $"סוג השדה \"{name}\" אינו נתמך.");
        }
    }

    private static bool TryParseDate(string text, out DateTime date)
    {
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        // accept an ISO date-time at midnight as well (e.g. from JSON serializers)
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date.TimeOfDay == TimeSpan.Zero;
    }

    private static bool InDateRange(DateTime value, FieldMeta field, string path, string name, Context ctx)
    {
        var range = field.Column.DateRange;
        if (range is null || (value >= range.Min && value <= range.Max)) return true;
        ctx.Error(path,
            $"התאריך {value:dd/MM/yyyy} חורג מהטווח המותר לשדה \"{name}\" " +
            $"(בין {range.Min:dd/MM/yyyy} ל-{range.Max:dd/MM/yyyy}).");
        return false;
    }

    /// <summary>Echo of user text inside messages is trimmed so a huge input cannot bloat responses.</summary>
    private static string Clip(string? text) =>
        text is null ? "" : text.Length <= 60 ? text : text[..60] + "…";

    private sealed class Context(MetadataCatalog catalog)
    {
        private readonly Dictionary<string, BoundJoin> _joinsByPath = new(StringComparer.Ordinal);

        public MetadataCatalog Catalog { get; } = catalog;
        public EntityMeta Root { get; set; } = null!;
        public List<ValidationError> Errors { get; } = [];
        public List<BoundJoin> Joins { get; } = [];
        public List<BoundRule> Rules { get; } = [];
        public int RuleCount { get; set; }

        public void Error(string path, string message) => Errors.Add(new ValidationError(path, message));

        public T? ErrorNull<T>(string path, string message) where T : class
        {
            Error(path, message);
            return null;
        }

        /// <summary>One join per distinct relation path, shared by all rules that use it.</summary>
        public BoundJoin GetOrAddJoin(BoundJoin? parent, RelationMeta relation, EntityMeta target)
        {
            var path = parent is null ? relation.RelationKey : $"{parent.Path}.{relation.RelationKey}";
            if (_joinsByPath.TryGetValue(path, out var existing)) return existing;

            var join = new BoundJoin(path, relation, target, parent);
            _joinsByPath.Add(path, join);
            Joins.Add(join);
            return join;
        }
    }
}
