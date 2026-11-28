using Microsoft.Extensions.Logging;

namespace RetroIde.Application.Emulators;

internal sealed class EmulatorSubscriptions(ILogger logger)
{
    private readonly object _gate = new();
    private readonly HashSet<Subscription> _active = [];
    private bool _closing;

    internal Subscription? Begin()
    {
        lock (_gate)
        {
            if (_closing)
                return null;
            var subscription = new Subscription(this);
            _active.Add(subscription);
            return subscription;
        }
    }

    internal Task DrainAsync()
    {
        Subscription[] active;
        lock (_gate)
        {
            _closing = true;
            active = [.. _active];
        }
        return Task.WhenAll(active.Select(static subscription => subscription.DisposeAsync().AsTask()));
    }

    private void Forget(Subscription subscription)
    {
        lock (_gate)
            _active.Remove(subscription);
    }

    internal sealed class Subscription : IAsyncDisposable
    {
        private readonly EmulatorSubscriptions _owner;
        private readonly TaskCompletionSource<IAsyncDisposable?> _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lazy<Task> _dispose;

        internal Subscription(EmulatorSubscriptions owner)
        {
            _owner = owner;
            _dispose = new(DisposeCoreAsync);
        }

        // Reserve the slot before opening the adapter resource so a closing session also drains in-flight opens.
        internal void SetOpened(IAsyncDisposable? resource) => _opened.SetResult(resource);

        public ValueTask DisposeAsync() => new(_dispose.Value);

        private async Task DisposeCoreAsync()
        {
            try
            {
                if (await _opened.Task.ConfigureAwait(false) is { } resource)
                    await resource.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                RuntimeFailures.Report(_owner.Logger, error, "release emulator subscription");
            }
            finally { _owner.Forget(this); }
        }
    }

    private ILogger Logger { get; } = logger;
}
