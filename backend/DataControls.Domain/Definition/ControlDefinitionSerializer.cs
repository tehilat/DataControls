using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataControls.Domain.Definition;

/// <summary>Single place that knows the JSON options of DefinitionJson.</summary>
public static class ControlDefinitionSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 64
    };

    /// <exception cref="DefinitionFormatException">The text is not a well-formed definition.</exception>
    public static ControlDefinition Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new DefinitionFormatException("הגדרת הבקרה ריקה.");

        try
        {
            return JsonSerializer.Deserialize<ControlDefinition>(json, Options)
                   ?? throw new DefinitionFormatException("הגדרת הבקרה ריקה.");
        }
        catch (JsonException ex)
        {
            var where = string.IsNullOrEmpty(ex.Path) ? "" : $" (מיקום: {ex.Path})";
            throw new DefinitionFormatException($"מבנה הגדרת הבקרה אינו תקין{where}.", ex);
        }
        catch (NotSupportedException ex)
        {
            // e.g. a node without "type" or an operand without "kind"
            throw new DefinitionFormatException(
                "מבנה הגדרת הבקרה אינו תקין: לכל צומת חייב להיות \"type\" ולכל ערך השוואה \"kind\".", ex);
        }
    }

    public static string Serialize(ControlDefinition definition) =>
        JsonSerializer.Serialize(definition, Options);
}

/// <summary>DefinitionJson could not be parsed. Message is user-facing (Hebrew).</summary>
public sealed class DefinitionFormatException(string message, Exception? inner = null)
    : Exception(message, inner);
