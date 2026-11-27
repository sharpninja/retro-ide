using System.Text.Json;
using RetroIde.Application.Git;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.LanguageServers;

internal readonly record struct LanguageProfileKey(Guid RootId, string ProfileId)
{
    internal const string DefaultProfile = "default";

    internal static bool ValidId(string? id) => id is { Length: > 0 and <= 64 } &&
        char.IsAsciiLetterOrDigit(id[0]) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

internal sealed record SavedLanguageProfile(Guid RootId, string ProfileId, string Executable, string[] Arguments,
    LanguageAssociation[] Languages, bool Enabled, JsonElement? InitializationOptions = null, JsonElement? Settings = null)
{
    internal LanguageProfileKey Key => new(RootId, ProfileId);

    internal bool IsValid() => LanguageProfileKey.ValidId(ProfileId) &&
        Executable is { Length: > 0 } && Path.IsPathFullyQualified(Executable) && !Executable.Contains('\0') &&
        Arguments is { Length: <= 64 } && Arguments.All(arg => arg is not null && !arg.Contains('\0')) &&
        Languages is { Length: > 0 and <= 32 } &&
        Languages.All(language => language is not null && !string.IsNullOrWhiteSpace(language.LanguageId) &&
            language.Extensions is { Length: > 0 and <= 32 } && language.Extensions.All(extension =>
                extension is { Length: > 1 and <= 32 } && extension[0] == '.' &&
                !extension.Contains('/') && !extension.Contains('\\') && !extension.Contains('\0'))) &&
        Extensions().Distinct(StringComparer.OrdinalIgnoreCase).Count() == Languages.Sum(language => language.Extensions.Length);

    internal bool Overlaps(SavedLanguageProfile other) =>
        Extensions().Intersect(other.Extensions(), StringComparer.OrdinalIgnoreCase).Any();

    internal LanguageServerConfiguration Bind(WorkspaceExplorer.Root root) =>
        new(RootId, root.Path, Executable, Arguments, Languages, InitializationOptions, Settings, ProfileId);

    private IEnumerable<string> Extensions() => Languages.SelectMany(language => language.Extensions);
}

internal sealed partial record LanguageServerConfiguration
{
    internal LanguageProfileKey Key => new(RootId, ProfileId);

    internal bool CanShareWith(LanguageServerConfiguration other) =>
        !NativeGit.ContainsPath(Directory, other.Directory) && !NativeGit.ContainsPath(other.Directory, Directory) &&
        string.Equals(Executable, other.Executable, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
        Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal) &&
        SameJson(InitializationOptions, other.InitializationOptions) && SameJson(Settings, other.Settings) &&
        MatchesAssociations(other) && other.MatchesAssociations(this);

    private bool MatchesAssociations(LanguageServerConfiguration other) => Languages.All(language =>
        language.Extensions.All(extension => string.Equals(language.LanguageId, other.MatchExtension(extension), StringComparison.Ordinal)));

    private static bool SameJson(JsonElement? left, JsonElement? right)
    {
        if (left is null || left.Value.ValueKind == JsonValueKind.Null)
            return right is null || right.Value.ValueKind == JsonValueKind.Null;
        return right is { } value && JsonElement.DeepEquals(left.Value, value);
    }
}

internal sealed class LanguageProfileConflictException() : InvalidOperationException("Language profiles in one root cannot claim the same extension.");
