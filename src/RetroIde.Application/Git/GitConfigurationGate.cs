namespace RetroIde.Application.Git;

/// <summary>Coordinates native Git transfers with built-in Git operations across all repositories used by the IDE process.</summary>
internal static class GitConfigurationGate
{
    // Git can include files from a different checkout, and a native hook can modify
    // shared configuration. The selected repository cannot bound those effects.
    private static readonly SemaphoreSlim Gate = new(1);

    internal static async Task<FeatureOutcome> RunAsync(CancellationToken token, Func<Task<FeatureOutcome>> operation)
    {
        using var held = await EnterAsync(token).ConfigureAwait(false);
        return await operation().ConfigureAwait(false);
    }

    internal static async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Gate.Release();
        }
    }
}
