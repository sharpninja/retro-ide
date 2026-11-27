using System.Collections.Concurrent;

namespace RetroIde.Application.Git;

/// <summary>Coordinates native Git transfers with built-in operations that can change their repository configuration.</summary>
internal static class GitConfigurationGate
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(Paths);

    internal static async Task<FeatureOutcome> RunAsync(GitWorktree repository, CancellationToken token, Func<Task<FeatureOutcome>> operation)
    {
        using var held = await EnterAsync(repository, token).ConfigureAwait(false);
        return await operation().ConfigureAwait(false);
    }

    internal static async Task<Lease> EnterAsync(GitWorktree repository, CancellationToken token, GitWorktree? related = null)
    {
        var primary = repository.CommonDirectory;
        var secondary = related?.CommonDirectory;
        var order = secondary is null ? 0 : Paths.Compare(primary, secondary);
        var first = Gates.GetOrAdd(order > 0 ? secondary! : primary, static _ => new SemaphoreSlim(1));
        var second = order == 0 ? null : Gates.GetOrAdd(order > 0 ? primary : secondary!, static _ => new SemaphoreSlim(1));

        // A submodule operation can change both repositories. Always take their gates
        // in the same order even when another operation already targets the child.
        await first.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (second is not null)
                await second.WaitAsync(token).ConfigureAwait(false);
            return new Lease(first, second);
        }
        catch
        {
            first.Release();
            throw;
        }
    }

    internal sealed class Lease(SemaphoreSlim first, SemaphoreSlim? second) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;
            second?.Release();
            first.Release();
        }
    }
}
