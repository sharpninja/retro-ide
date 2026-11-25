using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;

namespace RetroIde.Workbench;

/// <summary>Tracks logically blocked workbench scopes and applies the platform wait cursor to their live surfaces.</summary>
/// <remarks>TEST-RETRO-007#ac-test-system-wait-cursor</remarks>
public sealed class InteractionBlocks : IDisposable
{
    /// <summary>Identifies every workbench content surface, including floating panes.</summary>
    public const string WorkbenchScope = "workbench";

    private readonly Dictionary<string, int> _operations = new(StringComparer.Ordinal);
    private Cursor? _wait;
    private bool _disposed;

    internal event EventHandler? Changed;

    /// <summary>Reports whether any active operation blocks the scope.</summary>
    public bool IsBlocked(string scope)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _operations.ContainsKey(scope) || _operations.ContainsKey(WorkbenchScope);
    }

    /// <summary>Invokes the operation on the thread pool and tracks its optional block on the UI dispatcher.</summary>
    public async Task RunAsync(string? blockedScope, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        if (blockedScope is not null)
        {
            if (Dispatcher.UIThread.CheckAccess())
                Begin(blockedScope);
            else
                await Dispatcher.UIThread.InvokeAsync(() => Begin(blockedScope));
        }

        try { await Task.Run(() => operation(cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (blockedScope is not null)
                await Dispatcher.UIThread.InvokeAsync(() => End(blockedScope));
        }
    }

    /// <summary>Applies the wait override to a surface for its attached lifetime.</summary>
    public IDisposable Attach(string scope, Control surface)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SurfaceSubscription(this, scope, surface);
    }

    internal Control Wrap(string scope, Control content)
    {
        var overlay = new Border
        {
            Name = "InteractionSurface",
            Background = Avalonia.Media.Brushes.Transparent,
            IsVisible = false,
        };
        var region = new Grid { Children = { content, overlay } };
        IDisposable? subscription = null;
        region.AttachedToVisualTree += (_, _) => subscription = Attach(scope, overlay);
        region.DetachedFromVisualTree += (_, _) =>
        {
            subscription?.Dispose();
            subscription = null;
        };
        IDisposable? inputOverride = null;
        overlay.PropertyChanged += (_, args) =>
        {
            if (args.Property == InputElement.CursorProperty)
            {
                overlay.IsVisible = overlay.Cursor is not null;
                if (overlay.IsVisible)
                    inputOverride ??= content.SetValue(InputElement.IsEnabledProperty, false, BindingPriority.Animation);
                else
                {
                    inputOverride?.Dispose();
                    inputOverride = null;
                }
            }
        };
        return region;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed)
            return;

        _disposed = true;
        _operations.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        _wait?.Dispose();
    }

    private void Begin(string scope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _operations[scope] = _operations.GetValueOrDefault(scope) + 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void End(string scope)
    {
        if (!_operations.TryGetValue(scope, out var count))
            return;
        if (count == 1)
            _operations.Remove(scope);
        else
            _operations[scope] = count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SurfaceSubscription : IDisposable
    {
        private readonly InteractionBlocks _blocks;
        private readonly string _scope;
        private readonly Control _surface;
        private IDisposable? _cursorOverride;

        public SurfaceSubscription(InteractionBlocks blocks, string scope, Control surface)
        {
            _blocks = blocks;
            _scope = scope;
            _surface = surface;
            _blocks.Changed += Update;
            Update(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Dispatcher.UIThread.VerifyAccess();
            _blocks.Changed -= Update;
            _cursorOverride?.Dispose();
            _cursorOverride = null;
        }

        private void Update(object? sender, EventArgs args)
        {
            if (_blocks.IsBlocked(_scope))
            {
                var wait = _blocks._wait ??= new Cursor(StandardCursorType.Wait);
                _cursorOverride ??= _surface.SetValue(InputElement.CursorProperty, wait, BindingPriority.Animation);
            }
            else
            {
                _cursorOverride?.Dispose();
                _cursorOverride = null;
            }
        }
    }
}
