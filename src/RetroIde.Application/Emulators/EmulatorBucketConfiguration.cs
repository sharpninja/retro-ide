using System.Text.Json;
using System.Text.Json.Serialization;
using Source = RetroIde.Application.Emulators.EmulatorBucketCatalog.Source;

namespace RetroIde.Application.Emulators;

/// <summary>Stores Git bucket settings shared by the local workbenches.</summary>
internal sealed partial class EmulatorBucketConfiguration(string root)
{
    private const int SchemaVersion = 1;
    private const int MaximumBytes = 1024 * 1024;
    private readonly string _root = Path.GetFullPath(root);

    internal Source[] Read()
    {
        try
        {
            using var input = new FileStream(Path.Combine(_root, "buckets.json"), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            var length = input.Length;
            if (length > MaximumBytes)
                throw new InvalidDataException("The emulator bucket settings exceed their permitted size.");
            var bytes = new byte[(int)length];
            input.ReadExactly(bytes);
            var configuration = JsonSerializer.Deserialize(bytes, BucketJson.Default.Configuration);
            if (configuration is not { Schema: SchemaVersion, Buckets: not null })
                throw new InvalidDataException("The emulator bucket settings schema is invalid.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in configuration.Buckets)
            {
                if (source is null || !ids.Add(source.Id))
                    throw new InvalidDataException("The emulator bucket settings contain a missing or duplicate source.");
                EmulatorBucketCatalog.ValidateSource(source);
            }
            return configuration.Buckets;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return []; }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new InvalidDataException("The emulator bucket settings contain an invalid source.", error);
        }
    }

    internal Task ConfigureAsync(Source source, CancellationToken cancellationToken)
    {
        EmulatorBucketCatalog.ValidateSource(source);
        return ChangeAsync(current => current.Where(item => !string.Equals(item.Id, source.Id, StringComparison.Ordinal))
            .Append(source).OrderBy(static item => item.Id, StringComparer.Ordinal).ToArray(), cancellationToken);
    }

    internal Task RemoveAsync(string id, CancellationToken cancellationToken) =>
        ChangeAsync(current =>
        {
            var remaining = current.Where(item => !string.Equals(item.Id, id, StringComparison.Ordinal)).ToArray();
            return remaining.Length != current.Length ? remaining : throw new KeyNotFoundException("The emulator bucket is not configured.");
        }, cancellationToken);

    private async Task ChangeAsync(Func<Source[], Source[]> apply, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_root);
        using var access = await BundleFileLock.OpenExclusiveAsync(Path.Combine(_root, "buckets.lock"), cancellationToken).ConfigureAwait(false);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Configuration(SchemaVersion, apply(Read())), BucketJson.Default.Configuration);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("The emulator bucket settings exceed their permitted size.");

        var pending = Path.Combine(_root, $".buckets-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(pending, Path.Combine(_root, "buckets.json"), overwrite: true);
        }
        finally { File.Delete(pending); }
    }

    internal sealed record Configuration(int Schema, Source[] Buckets);

    [JsonSerializable(typeof(Configuration))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    private sealed partial class BucketJson : JsonSerializerContext;
}
