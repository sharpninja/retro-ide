namespace RetroIde.Application;

/// <summary>Coordinates reviewed native Git operations with writes that can change their filesystem inputs.</summary>
internal static class WorkspaceMutationGate
{
    // Git includes can point into another checkout, including files open in the editor.
    private static readonly SemaphoreSlim Gate = new(1);

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
