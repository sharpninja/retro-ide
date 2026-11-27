using System.Text.Json;

namespace RetroIde.Application.Emulators;

internal sealed record DebuggerPresentationProfile(string Id, DebuggerPresentationProfile.AddressStyle Disassembly,
    DebuggerPresentationProfile.AddressStyle MemoryAddress,
    int BytesPerRow, int BytesPerGroup)
{
    internal const int CurrentVersion = 1;
    internal const int MaximumBytes = 64 * 1024;

    internal static DebuggerPresentationProfile? Read(ReadOnlyMemory<byte> bytes, string id, int version)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = Object(json.RootElement);
            if (!string.Equals(Text(root, "id"), id, StringComparison.Ordinal) || Number(root, "schemaVersion", 1, int.MaxValue) != version)
                throw Invalid("The ID or schema version does not match the bundle manifest.");
            if (version != CurrentVersion)
                return null; // The envelope is verified. A newer schema is optional to this client.

            var disassembly = ReadAddress(root.GetProperty("disassembly"));
            var memory = Object(root.GetProperty("memory"));
            var bytesPerRow = Number(memory, "bytesPerRow", 1, 256);
            var bytesPerGroup = Number(memory, "bytesPerGroup", 1, 16);
            if (bytesPerRow % bytesPerGroup != 0)
                throw Invalid("The memory group size must divide the row size.");
            return new(id, disassembly, ReadAddress(memory.GetProperty("address")), bytesPerRow, bytesPerGroup);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("The debugger presentation profile is not valid JSON or is missing a required value.", error);
        }
    }

    private static AddressStyle ReadAddress(JsonElement element)
    {
        var value = Object(element);
        var radix = Number(value, "radix", 10, 16);
        var prefix = Text(value, "prefix");
        var notation = Text(value, "notation");
        if (radix is not (10 or 16) || prefix is not ("" or "$" or "0x" or "0X" or "&H" or "&h") ||
            (radix == 10 && prefix.Length != 0) || notation is not ("flat" or "bank-offset" or "segment-offset"))
            throw Invalid("The address radix, prefix, or notation is unsupported.");
        return new(radix, prefix, Number(value, "minimumDigits", 1, 32), value.GetProperty("upperCase").GetBoolean(), notation);
    }

    private static JsonElement Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("An object is required.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (value.EnumerateObject().Any(property => !names.Add(property.Name)))
            throw Invalid("Duplicate property names are not permitted.");
        return value;
    }

    private static string Text(JsonElement value, string name) =>
        value.GetProperty(name).GetString() ?? throw Invalid($"A string is required for '{name}'.");

    private static int Number(JsonElement value, string name, int min, int max) =>
        value.GetProperty(name).TryGetInt32(out var number) && number >= min && number <= max
            ? number : throw Invalid($"'{name}' is out of range.");

    private static InvalidDataException Invalid(string message) => new("Invalid debugger presentation profile. " + message);

    internal sealed record AddressStyle(int Radix, string Prefix, int MinimumDigits, bool UpperCase, string Notation);
}
