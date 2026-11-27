using System.Text;
using RetroIde.Application.Git;

namespace RetroIde.Application.Terminals;

internal interface ITerminalRegistration
{
    Task<FeatureOutcome> OpenTerminalAsync(GitSelection selection, CancellationToken token);
    TerminalFeatures.Session[] ListTerminals();
    RepositoryTerminal? FindTerminal(Guid viewKey);
    bool TryCloseTerminal(Guid viewKey, out Task shutdown);
}

internal sealed class TerminalFeatures
{
    internal sealed record OpenInput(string WorktreePath);
    internal sealed record SessionInput(Guid ViewKey);
    internal sealed record ReadInput(Guid ViewKey, long Offset = 0, int MaximumBytes = 16384);
    internal sealed record WriteInput(Guid ViewKey, string Text);
    internal sealed record ResizeInput(Guid ViewKey, int Columns, int Rows);
    internal sealed record Session(Guid ViewKey, Guid WorkspaceId, string WorktreePath, int ProcessId, bool IsRunning, int? ExitCode, int? ExitSignal);

    public Feature<OpenInput> Open { get; } = new("git.terminal.open", "git_terminal_open",
        "Open an interactive terminal document in the selected full Git worktree using the user's native shell.",
        OpenAsync, typeof(Session), mutation: true);

    public Feature<GetContextInput> List { get; } = new("git.terminal.list", "git_terminal_list",
        "List terminal documents and live process state for the attached workbench.", false,
        static (_, view) => view is ITerminalRegistration terminals
            ? FeatureOutcome.Success("Repository terminals read.", terminals.ListTerminals()) : Unavailable(), typeof(Session[]));

    public Feature<ReadInput> Read { get; } = new("git.terminal.read", "git_terminal_read",
        "Read a bounded page of retained terminal bytes from an absolute byte offset. Bytes are encoded as base64.", false,
        ReadTranscript, typeof(TerminalTranscript.Slice));

    public Feature<WriteInput> Write { get; } = new("git.terminal.write", "git_terminal_write",
        "Send UTF-8 input verbatim to an open terminal. Send a carriage return to submit a shell command.",
        static (input, view, _) => Prepare(view, input.ViewKey, async (terminal, token) =>
        {
            if (input.Text is null || Encoding.UTF8.GetByteCount(input.Text) is 0 or > 32768)
                return new("invalid_arguments", "Provide 1 to 32768 bytes of UTF-8 terminal input.");
            await terminal.WriteAsync(Encoding.UTF8.GetBytes(input.Text), token).ConfigureAwait(false);
            return new("ok", "Terminal input delivered.");
        }), typeof(object), mutation: true);

    public Feature<ResizeInput> Resize { get; } = new("git.terminal.resize", "git_terminal_resize",
        "Set the native terminal size in character columns and rows.",
        static (input, view, _) => Prepare(view, input.ViewKey, async (terminal, token) =>
        {
            if (input.Columns is < 1 or > 1000 || input.Rows is < 1 or > 1000)
                return new("invalid_arguments", "Terminal dimensions must each be between 1 and 1000.");
            await terminal.ResizeAsync(input.Columns, input.Rows, token).ConfigureAwait(false);
            return new("ok", "Terminal resized.");
        }), typeof(object), mutation: true);

    public Feature<SessionInput> Close { get; } = new("git.terminal.close", "git_terminal_close",
        "Close a terminal document in the attached workbench and stop its native shell.",
        static (input, view, _) => Task.FromResult(view is ITerminalRegistration && input.ViewKey != Guid.Empty
            ? new FeatureOutcome("ok", "Terminal close requested.")
            {
                Commit = current => current is ITerminalRegistration terminals && terminals.TryCloseTerminal(input.ViewKey, out var shutdown)
                    ? new FeatureOutcome("ok", "Terminal closing.")
                    {
                        ExecuteAuthorizedAsync = async _ =>
                        {
                            await shutdown.ConfigureAwait(false);
                            return new("ok", "Terminal closed.");
                        },
                    }
                    : Unavailable(),
            }
            : Unavailable()), typeof(object), mutation: true);

    private static Task<FeatureOutcome> OpenAsync(OpenInput input, IWorkbenchRegistration view, CancellationToken token) =>
        GitOperationRecovery.WithSelectionAsync(input.WorktreePath, view, (_, selection) => Task.FromResult(
            new FeatureOutcome("ok", "Repository terminal requested.")
            {
                Commit = current => current is ITerminalRegistration terminals && GitOperationRecovery.SameSelection(current, selection)
                    ? new FeatureOutcome("ok", "Repository terminal authorized.")
                    {
                        ExecuteAuthorizedAsync = cancellation => terminals.OpenTerminalAsync(selection, cancellation),
                    }
                    : GitOperationRecovery.ChangedSelection(),
            }), token);

    private static FeatureOutcome ReadTranscript(ReadInput input, IWorkbenchRegistration view)
    {
        if ((view as ITerminalRegistration)?.FindTerminal(input.ViewKey) is not { } terminal)
            return Unavailable();
        if (input.Offset < 0 || input.MaximumBytes is < 1 or > 65536)
            return new("invalid_arguments", "Provide a nonnegative offset and a page size between 1 and 65536 bytes.");
        try { return FeatureOutcome.Success("Terminal output read.", terminal.Read(input.Offset, input.MaximumBytes)); }
        catch (ArgumentOutOfRangeException)
        {
            return new("invalid_arguments", "The offset exceeds the terminal output received so far.");
        }
    }

    private static Task<FeatureOutcome> Prepare(IWorkbenchRegistration view, Guid id,
        Func<RepositoryTerminal, CancellationToken, Task<FeatureOutcome>> action)
    {
        if ((view as ITerminalRegistration)?.FindTerminal(id) is not { } target)
            return Task.FromResult(Unavailable());
        return Task.FromResult(new FeatureOutcome("ok", "Terminal operation requested.")
        {
            Commit = current => ReferenceEquals((current as ITerminalRegistration)?.FindTerminal(id), target)
                ? new FeatureOutcome("ok", "Terminal operation authorized.")
                {
                    ExecuteAuthorizedAsync = token => UseRunningAsync(target, action, token),
                }
                : Unavailable(),
        });
    }

    private static async Task<FeatureOutcome> UseRunningAsync(RepositoryTerminal target,
        Func<RepositoryTerminal, CancellationToken, Task<FeatureOutcome>> action, CancellationToken token)
    {
        if (!target.IsRunning)
            return new("terminal_exited", "The terminal process has finished.");
        try { return await action(target, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !target.IsRunning)
        {
            return new("terminal_exited", "The terminal process has finished.");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            target.ReportFailure(error, "Repository terminal operation");
            return target.IsRunning
                ? new("terminal_io_failed", "The terminal input or resize could not be delivered.")
                : new("terminal_exited", "The terminal process has finished.");
        }
    }

    private static FeatureOutcome Unavailable() => new("terminal_unavailable", "The terminal is unavailable in this workbench.");
}
