using DataControls.Application.Binding;
using DataControls.Application.Querying;
using DataControls.Domain.Definition;
using DataControls.Infrastructure.Querying;

namespace DataControls.Tests.Support;

/// <summary>Small helpers to write definitions as JSON in tests and push them through the pipeline.</summary>
public static class Defs
{
    public static readonly Domain.Metadata.MetadataCatalog Catalog = TestCatalog.Create();

    /// <summary>Wraps a condition node (JSON) into a full definition.</summary>
    public static string Json(string rootEntity, string conditionJson) =>
        $$"""{"version":1,"rootEntity":"{{rootEntity}}","condition":{{conditionJson}}}""";

    public static string Rule(string field, string op, string? compareTo = null) =>
        compareTo is null
            ? $$"""{"type":"rule","field":"{{field}}","operator":"{{op}}"}"""
            : $$"""{"type":"rule","field":"{{field}}","operator":"{{op}}","compareTo":{{compareTo}}}""";

    public static string Value(string jsonScalar) => $$"""{"kind":"value","value":{{jsonScalar}}}""";
    public static string Field(string path) => $$"""{"kind":"field","field":"{{path}}"}""";
    public static string Today(int offset = 0) => $$"""{"kind":"today","offsetDays":{{offset}}}""";
    public const string Now = """{"kind":"now"}""";

    public static string Group(string logic, params string[] children) =>
        $$"""{"type":"group","logic":"{{logic}}","children":[{{string.Join(",", children)}}]}""";

    public static BoundDefinition Bind(string definitionJson) =>
        new DefinitionBinder().Bind(ControlDefinitionSerializer.Parse(definitionJson), Catalog);

    public static BuiltQuery Build(string definitionJson, int maxRows = 1000) =>
        new SqlQueryBuilder().Build(Bind(definitionJson), maxRows);
}
