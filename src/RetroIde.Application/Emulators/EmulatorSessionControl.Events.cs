using System.Runtime.CompilerServices;
using RetroIde.Emulation;

namespace RetroIde.Application.Emulators;

internal sealed partial class EmulatorSessionControl
{
    internal async IAsyncEnumerable<AdapterResult<SessionEvent>> WatchAsync(EventCursor? after,
        [EnumeratorCancellation] CancellationToken token)
    {
        var start = State;
        var cursor = after ?? start.Cursor;
        if (cursor.Session != _identity)
        {
            yield return Fail<SessionEvent>(AdapterErrorCodes.Incompatible, "The cursor belongs to a different emulator session.");
            yield break;
        }

        ulong? revision = cursor == start.Cursor ? start.StateRevision : null;
        while (!token.IsCancellationRequested)
        {
            SessionEventGap? gap = null;
            await foreach (var item in ReadSubscriptionAsync(cursor, revision, token).ConfigureAwait(false))
            {
                yield return item;
                if (item.Value is SessionEventGap missed)
                {
                    gap = missed;
                    break;
                }
            }

            if (gap is null)
                yield break;

            var refreshed = await ReadAsync(token).ConfigureAwait(false);
            if (!refreshed.Succeeded)
            {
                yield return AdapterResult<SessionEvent>.Failure(refreshed.Error);
                yield break;
            }
            var state = refreshed.Value;
            if (state.Cursor.Sequence < gap.Cursor.Sequence || state.StateRevision < gap.StateRevision)
            {
                yield return Fail<SessionEvent>(AdapterErrorCodes.Incompatible, "The emulator could not provide a state snapshot covering the event gap.");
                yield break;
            }

            cursor = state.Cursor;
            revision = state.StateRevision;
            yield return AdapterResult<SessionEvent>.Success(new SessionStateChanged(state));
        }
    }

    private async IAsyncEnumerable<AdapterResult<SessionEvent>> ReadSubscriptionAsync(EventCursor cursor, ulong? revision,
        [EnumeratorCancellation] CancellationToken token)
    {
        var subscription = Subscribe(cursor, token);
        if (!subscription.Succeeded)
        {
            yield return AdapterResult<SessionEvent>.Failure(subscription.Error);
            yield break;
        }

        var reader = subscription.Value;
        try
        {
            while (await ReadNextAsync(reader, token).ConfigureAwait(false) is { } item)
            {
                if (!item.Succeeded)
                {
                    yield return item;
                    yield break;
                }
                var observed = item.Value;
                if (!ValidEvent(observed, cursor, revision))
                {
                    yield return Fail<SessionEvent>(AdapterErrorCodes.Incompatible, "The emulator returned an invalid event or cursor.");
                    yield break;
                }
                if (observed is SessionEventGap || observed.Cursor.Sequence - cursor.Sequence != 1)
                {
                    yield return AdapterResult<SessionEvent>.Success(observed as SessionEventGap ??
                        new SessionEventGap(observed.Cursor, observed.StateRevision));
                    yield break;
                }

                cursor = observed.Cursor;
                revision = observed.StateRevision;
                if (observed is SessionStateChanged changed)
                    Publish(changed.State);
                yield return item;
            }
        }
        finally { await EndSubscriptionAsync(reader, token).ConfigureAwait(false); }
    }

    private bool ValidEvent(SessionEvent observed, EventCursor after, ulong? revision) =>
        observed.Cursor.Session == _identity && observed.Cursor.Sequence > after.Sequence &&
        (revision is null || observed.StateRevision >= revision) &&
        (observed is not SessionStateChanged changed ||
            (Matches(changed.State) && changed.State.Cursor == observed.Cursor && changed.State.StateRevision == observed.StateRevision));

    private AdapterResult<IAsyncEnumerator<SessionEvent>> Subscribe(EventCursor cursor, CancellationToken token)
    {
        try
        {
            return AdapterResult<IAsyncEnumerator<SessionEvent>>.Success(_connection.ReadEventsAsync(cursor, token).GetAsyncEnumerator(token));
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            return EventReadFailed<IAsyncEnumerator<SessionEvent>>(error, token);
        }
    }

    private async ValueTask<AdapterResult<SessionEvent>?> ReadNextAsync(IAsyncEnumerator<SessionEvent> reader, CancellationToken token)
    {
        try
        {
            return await reader.MoveNextAsync().ConfigureAwait(false) ? AdapterResult<SessionEvent>.Success(reader.Current) : null;
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            return EventReadFailed<SessionEvent>(error, token);
        }
    }

    private async ValueTask EndSubscriptionAsync(IAsyncEnumerator<SessionEvent> reader, CancellationToken token)
    {
        try { await reader.DisposeAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            RuntimeFailures.Report(_logger, error, "release emulator event subscription");
        }
    }

    private AdapterResult<T> EventReadFailed<T>(Exception error, CancellationToken token) where T : class
    {
        if (error is OperationCanceledException && token.IsCancellationRequested)
            return Fail<T>(AdapterErrorCodes.Cancelled, "The emulator event subscription was cancelled.");

        RuntimeFailures.Report(_logger, error, "read emulator events");
        return Fail<T>(AdapterErrorCodes.Disconnected, "The emulator event subscription disconnected.");
    }
}
