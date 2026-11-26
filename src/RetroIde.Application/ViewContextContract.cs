using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;

namespace RetroIde.Application;

/// <summary>Opts a read-only ViewModel property into the MCP context contract.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-viewmodel-context</remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class McpContextAttribute : Attribute;

/// <summary>Identifies a live workbench registration and its captured state.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-viewmodel-context</remarks>
public sealed record WorkbenchAddress(Guid ViewKey, Guid WorkspaceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ActiveToolId, long WorkbenchRevision);

/// <summary>Defines the shared context and operation JSON conventions.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-viewmodel-context</remarks>
public static class WireJson
{
    /// <summary>Serializes canonical camel-case names and rejects extra input members.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>Builds discovery and snapshots from the same explicit ViewModel property list.</summary>
/// <remarks>TEST-RETRO-015#ac-test-mcp-viewmodel-context</remarks>
public sealed class ViewContextContract
{
    private readonly (string Name, PropertyInfo Property)[] _members;
    private readonly string[] _memberNames;

    /// <summary>Loads and validates the workbench ViewModel's public context metadata.</summary>
    public ViewContextContract(Type viewModelType)
        : this(viewModelType, typeof(WorkbenchAddress))
    {
    }

    internal ViewContextContract(Type viewModelType, Type addressType)
    {
        var nullability = new NullabilityInfoContext();
        _members = viewModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(McpContextAttribute), inherit: true))
            .Select(p => (Name: JsonNamingPolicy.CamelCase.ConvertName(p.Name), Property: p))
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        _memberNames = _members.Select(member => member.Name).ToArray();

        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, property) in _members)
        {
            if (property.GetMethod is not { IsPublic: true } || property.SetMethod is { IsPublic: true } || property.GetIndexParameters().Length != 0)
                throw new InvalidOperationException($"Context property {property.Name} must be publicly readable and externally read-only.");

            var nullable = nullability.Create(property).ReadState == NullabilityState.Nullable;
            var schema = WireJson.Options.GetJsonSchemaAsNode(property.PropertyType,
                new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = !nullable });
            if (name is "viewKey" or "workspaceId" or "documentId" or "rootId")
                schema["not"] = new JsonObject { ["const"] = Guid.Empty.ToString() };
            if (name is "workbenchRevision" or "revision")
                schema["minimum"] = 0;
            properties[name] = schema;
            required.Add(name);
        }

        var addressFields = addressType.GetProperties().ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), StringComparer.Ordinal);
        if (_members.Length != addressFields.Count ||
            _members.Any(member => !addressFields.TryGetValue(member.Name, out var field) || field.PropertyType != member.Property.PropertyType ||
                nullability.Create(field).ReadState != nullability.Create(member.Property).ReadState))
            throw new InvalidOperationException("The typed address must match the exposed ViewModel properties.");

        Schema = JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        });
    }

    /// <summary>Gets the closed JSON Schema for the exposed properties.</summary>
    public JsonElement Schema { get; }

    /// <summary>Captures a detached snapshot. Live callers must run on the ViewModel's dispatcher.</summary>
    public JsonElement Capture(object view)
    {
        var snapshot = new JsonObject();
        foreach (var (name, property) in _members)
            snapshot[name] = JsonSerializer.SerializeToNode(property.GetValue(view), property.PropertyType, WireJson.Options);

        return JsonSerializer.SerializeToElement(snapshot);
    }

    /// <summary>Parses a captured context, rejecting missing, repeated, or undeclared keys.</summary>
    public bool TryRead(JsonElement snapshot, out WorkbenchAddress? address)
    {
        address = null;
        if (snapshot.ValueKind != JsonValueKind.Object ||
            !snapshot.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(_memberNames, StringComparer.Ordinal))
            return false;

        try
        {
            var candidate = snapshot.Deserialize<WorkbenchAddress>(WireJson.Options);
            if (candidate is not { ViewKey: var viewKey, WorkspaceId: var workspace, WorkbenchRevision: >= 0 }
                || viewKey == Guid.Empty || workspace == Guid.Empty)
                return false;

            address = candidate;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
