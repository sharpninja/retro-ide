using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pin = RetroIde.Application.Emulators.EmulatorBundleStore.Pin;

namespace RetroIde.Application.Emulators;

/// <summary>Persists portable project pins and verifies the installed version before a selection changes.</summary>
/// <remarks>FR-RETRO-009#ac-version-pins; TR-RETRO-BUNDLE-009#ac-bundle-runtime-resolution.</remarks>
internal sealed partial class EmulatorProjectSelectionStore(string projectRoot, string stateRoot, EmulatorBundleStore bundles)
{
    private const int CurrentSchema = 1;
    private const int MaximumBytes = 1024 * 1024;
    private readonly string _project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
    private readonly string _locks = Path.Combine(Path.GetFullPath(stateRoot), "project-locks");

    internal Snapshot? Read()
    {
        var directory = Path.Combine(_project, ".retroide");
        if (!ExistsAs(directory, directoryExpected: true))
            return null;
        var path = Path.Combine(directory, "emulator.json");
        if (!ExistsAs(path, directoryExpected: false))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var length = stream.Length;
            if (length > MaximumBytes)
                throw new InvalidDataException("The emulator project selection exceeds its permitted size.");

            var bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            var value = JsonSerializer.Deserialize(bytes, SelectionJson.Default.Snapshot);
            if (value is not { SchemaVersion: CurrentSchema, Active: not null, Previous: not null })
                throw new InvalidDataException("The emulator project selection schema is invalid.");
            var active = value.Active.Normalized();
            var history = new List<Pin>();
            var known = new HashSet<Pin> { active };
            foreach (var previous in value.Previous)
            {
                var pin = previous?.Normalized();
                if (pin is null || !known.Add(pin))
                    throw new InvalidDataException("The emulator project selection contains invalid or duplicate history.");
                history.Add(pin);
            }
            return new Snapshot(CurrentSchema, active, history.AsReadOnly());
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new InvalidDataException("The emulator project selection contains an invalid pin.", error);
        }
    }

    internal Task<Snapshot> SelectAsync(Pin pin, string runtimeIdentifier, Version adapterApi, CancellationToken cancellationToken) =>
        ChangeAsync(pin, runtimeIdentifier, adapterApi, requirePrevious: false, cancellationToken);

    internal Task<Snapshot> RollbackAsync(Pin pin, string runtimeIdentifier, Version adapterApi, CancellationToken cancellationToken) =>
        ChangeAsync(pin, runtimeIdentifier, adapterApi, requirePrevious: true, cancellationToken);

    internal EmulatorBundleStore.BundleLease? AcquireActive(string runtimeIdentifier, Version adapterApi)
    {
        var selection = Read();
        return selection is null ? null : Acquire(selection.Active, runtimeIdentifier, adapterApi);
    }

    private async Task<Snapshot> ChangeAsync(Pin pin, string runtimeIdentifier, Version adapterApi, bool requirePrevious,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pin = pin.Normalized();
        if (!Directory.Exists(_project))
            throw new DirectoryNotFoundException("The selected project folder no longer exists.");
        Directory.CreateDirectory(_locks);
        var identity = OperatingSystem.IsWindows() ? _project.Replace('\\', '/').ToUpperInvariant() : _project;
        var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".lock";
        using var access = await BundleFileLock.OpenExclusiveAsync(Path.Combine(_locks, name), cancellationToken).ConfigureAwait(false);
        var current = Read();
        if (requirePrevious && (current is null || !current.Previous.Contains(pin)))
            throw new InvalidOperationException("The selected bundle is not in this project's previous selections.");
        using var bundle = Acquire(pin, runtimeIdentifier, adapterApi);
        if (current is not null && current.Active == pin)
            return current;

        IReadOnlyList<Pin> previous = current is null ? [] :
            Array.AsReadOnly(current.Previous.Prepend(current.Active).Where(item => item != pin).ToArray());
        var selection = new Snapshot(CurrentSchema, pin, previous);
        Write(selection, cancellationToken);
        return selection;
    }

    private EmulatorBundleStore.BundleLease Acquire(Pin pin, string runtimeIdentifier, Version adapterApi) =>
        bundles.Acquire(pin, runtimeIdentifier, adapterApi)
            ?? throw new InvalidOperationException("The selected bundle version is not installed for this host.");

    private void Write(Snapshot selection, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(selection, SelectionJson.Default.Snapshot);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("The emulator project selection exceeds its permitted size.");

        var directory = Path.Combine(_project, ".retroide");
        if (!ExistsAs(directory, directoryExpected: true))
            Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "emulator.json");
        _ = ExistsAs(path, directoryExpected: false);
        var pending = Path.Combine(directory, $".emulator-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(pending, path, overwrite: true);
        }
        finally
        {
            File.Delete(pending);
        }
    }

    private static bool ExistsAs(string path, bool directoryExpected)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Directory) != directoryExpected)
                throw new InvalidDataException("The emulator project selection path has an unexpected file type.");
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    internal sealed record Snapshot(int SchemaVersion, Pin Active, IReadOnlyList<Pin> Previous);

    [JsonSerializable(typeof(Snapshot))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    private sealed partial class SelectionJson : JsonSerializerContext;
}
