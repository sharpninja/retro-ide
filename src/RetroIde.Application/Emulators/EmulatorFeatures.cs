using System.ComponentModel;

namespace RetroIde.Application.Emulators;

internal sealed class EmulatorFeatures
{
    internal sealed record ConfigureInput(string BucketId, string Repository);
    internal sealed record BucketInput(string BucketId);

    internal Feature<GetContextInput> List { get; } = new("emulator.buckets.list", "emulator_list_buckets",
        "Read configured emulator buckets, their verified cached releases, and host compatibility.",
        static (_, view, token) => view is IEmulatorRegistration { Emulators: { } workspace }
            ? RunAsync(workspace, static (emulators, cancellation) => emulators.ReadAsync(cancellation), "Emulator buckets read.", token)
            : Task.FromResult(Unavailable()), typeof(EmulatorWorkspace.CatalogState));

    internal Feature<ConfigureInput> Configure { get; } = new("emulator.bucket.configure", "emulator_configure_bucket",
        "Add or update an emulator bucket using an absolute Git repository URL.",
        static (input, view, _) => Prepare(view, (workspace, token) => workspace.ConfigureAsync(input.BucketId, input.Repository, token),
            "Emulator bucket configured."), typeof(EmulatorWorkspace.BucketState), mutation: true);

    internal Feature<BucketInput> Refresh { get; } = new("emulator.bucket.refresh", "emulator_refresh_bucket",
        "Refresh a configured Git bucket and publish its catalog after verifying every manifest.",
        static (input, view, _) => Prepare(view, (workspace, token) => workspace.RefreshAsync(input.BucketId, token),
            "Emulator bucket refreshed."), typeof(EmulatorWorkspace.BucketState), mutation: true, background: true);

    internal Feature<BucketInput> Remove { get; } = new("emulator.bucket.remove", "emulator_remove_bucket",
        "Remove a configured emulator bucket and retain its installed bundles and project pins.",
        static (input, view, _) => Prepare(view, (workspace, token) => workspace.RemoveAsync(input.BucketId, token),
            "Emulator bucket removed."), typeof(EmulatorWorkspace.BucketChange), mutation: true);

    private static Task<FeatureOutcome> Prepare<T>(IWorkbenchRegistration view,
        Func<EmulatorWorkspace, CancellationToken, Task<T>> execute, string success)
    {
        if (view is not IEmulatorRegistration { Emulators: { } workspace })
            return Task.FromResult(Unavailable());
        return Task.FromResult(new FeatureOutcome("ok", "Emulator bucket operation prepared.")
        {
            Commit = current => current is IEmulatorRegistration active && ReferenceEquals(active.Emulators, workspace)
                ? new FeatureOutcome("ok", "Emulator bucket operation authorized.")
                {
                    ExecuteAuthorizedAsync = token => RunAsync(workspace, execute, success, token),
                }
                : Unavailable(),
        });
    }

    private static async Task<FeatureOutcome> RunAsync<T>(EmulatorWorkspace workspace,
        Func<EmulatorWorkspace, CancellationToken, Task<T>> execute, string success, CancellationToken token)
    {
        try
        {
            var result = await execute(workspace, token).ConfigureAwait(false);
            return FeatureOutcome.Success(success, result);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException
            or KeyNotFoundException or Win32Exception)
        {
            workspace.ReportFailure(error);
            return error switch
            {
                KeyNotFoundException => new("emulator_bucket_missing", "The emulator bucket is not configured."),
                InvalidDataException or ArgumentException => new("emulator_bucket_invalid", "The emulator bucket settings or manifest are invalid."),
                _ => new("emulator_bucket_io", "The emulator bucket could not be read or updated."),
            };
        }
    }

    private static FeatureOutcome Unavailable() => new("emulators_unavailable", "Emulator bundle management is unavailable.");
}
