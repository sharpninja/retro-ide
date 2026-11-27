using Avalonia.Threading;
using RetroIde.Application;
using RetroIde.Application.Terminals;

namespace RetroIde.Workbench;

internal sealed partial class DocumentController
{
    private const int MaximumTerminals = 8;
    private readonly Dictionary<Guid, ShellDocument> _terminals = [];
    private readonly Dictionary<Guid, Task> _closingTerminals = [];
    private bool _terminalsClosing;
    private int _openingTerminals;

    internal bool TryReserveTerminal()
    {
        if (_terminalsClosing || _terminals.Count + _openingTerminals >= MaximumTerminals)
            return false;
        _openingTerminals++;
        return true;
    }

    internal void ReleaseTerminalReservation() => _openingTerminals--;

    internal TerminalFeatures.Session? ShowTerminal(RepositoryTerminal terminal)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_terminalsClosing)
            return null;
        var view = new TerminalViewModel(_workbench, _features.Terminals, terminal);
        if (!AddOrUpdateView(view))
            return null;
        view.CommandRequested += OnTerminalCommandRequested;
        var document = new ShellDocument(view.Scope, view.Title, "", _interaction)
        {
            GitTerminal = view,
            CanClose = true,
            CanFloat = true,
        };
        _terminals.Add(view.ViewKey, document);
        _layout.AddTransientDocument(document);
        _layout.ActivateDocument(document);
        return view.Capture();
    }

    internal TerminalFeatures.Session[] ListTerminals() => _terminals.Values.Select(item => item.GitTerminal!.Capture()).ToArray();

    internal RepositoryTerminal? FindTerminal(Guid viewKey) => _terminals.GetValueOrDefault(viewKey)?.GitTerminal?.Terminal;

    internal bool TryCloseTerminal(Guid viewKey, out Task shutdown)
    {
        shutdown = Task.CompletedTask;
        if (!_terminals.TryGetValue(viewKey, out var document))
            return false;
        var previous = _closingView;
        _closingView = viewKey;
        try { _layout.Factory.CloseDockable(document); }
        finally { _closingView = previous; }
        if (!_closingTerminals.TryGetValue(viewKey, out var closing))
            return false;
        shutdown = closing;
        return true;
    }

    internal Task DrainTerminalsAsync()
    {
        _terminalsClosing = true;
        foreach (var document in _terminals.Values.ToArray())
            RemoveTerminal(document);
        return Task.WhenAll(_closingTerminals.Values);
    }

    private void RemoveTerminal(ShellDocument document)
    {
        if (document.GitTerminal is not { } view || !_terminals.Remove(view.ViewKey))
            return;
        view.CommandRequested -= OnTerminalCommandRequested;
        RemoveView(view);
        _layout.ForgetTransientDocument(document);
        foreach (var (key, closing) in _closingTerminals.ToArray())
            if (closing.IsCompleted)
                _closingTerminals.Remove(key);
        _closingTerminals.Add(view.ViewKey, DisposeTerminalAsync(view.Terminal));
    }

    private static async Task DisposeTerminalAsync(RepositoryTerminal terminal)
    {
        try { await terminal.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            terminal.ReportFailure(error, "Close repository terminal");
        }
    }

    private void OnTerminalCommandRequested(object? sender, ViewCommandRequested request)
    {
        if (request.Completion is not null || sender is not TerminalViewModel view || request.OriginViewKey != view.ViewKey ||
            !ReferenceEquals(GetView<IView>(view.ViewKey), view))
            return;
        var close = _features.Terminals.Close;
        if (request.Payload is TerminalFeatures.SessionInput input && input.ViewKey == view.ViewKey &&
            string.Equals(request.CommandId, close.Id, StringComparison.Ordinal))
            request.Completion = CompleteAsync(view, close, input, request);
    }
}
