using System.Text;
using Avalonia.Controls;
using Avalonia.RemoteControl.Protocol.V1;
using Avalonia.RemoteControl.Server;
using Avalonia.RemoteControl.Server.Input;
using Avalonia.RemoteControl.Server.Threading;
using Iciclecreek.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static partial class RemoteControlGitTests
{
    private static async Task VerifyTerminalAsync(WorkbenchWindow window, WorkbenchSession session, GitWorktreeFixture sample)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        const string commandId = "git.terminal.open.pane";
        var worktree = session.SourceControl.Repository?.WorktreePath ?? throw new InvalidOperationException("Select a Git worktree.");
        var head = (await sample.GitAsync(worktree, token, "rev-parse", "--short", "HEAD").ConfigureAwait(true)).Trim();
        (await FindAsync<Button>(window, commandId, token).ConfigureAwait(true)).BringIntoView();
        await WorkbenchRemoteControl.ClickAsync(window, commandId).ConfigureAwait(true);
        await (session.SourceControl.OpenTerminalCommand.ExecutionTask ??
            throw new InvalidOperationException("Source Control did not dispatch Open Repository Terminal.")).WaitAsync(token).ConfigureAwait(true);
        var terminal = await FindAsync<TerminalControl>(window, TerminalSurface.AutomationId, token).ConfigureAwait(true);
        while (!terminal.IsLive)
            await Task.Delay(20, token).ConfigureAwait(true);
        var document = (ShellDocument)terminal.DataContext!;
        var view = document.GitTerminal ?? throw new InvalidOperationException("The terminal has no registered document view.");
        var input = new RemoteControlInputDispatcher(new StaticRemoteControlRootProvider(window),
            Options.Create(new AvaloniaRemoteControlOptions { AllowRemoteActions = true, AllowRemoteInput = true }),
            new AvaloniaUiThreadRemoteControlDispatcher(), NullLogger<RemoteControlInputDispatcher>.Instance);

        var rendered = new StringBuilder();
        terminal.OutputReceived += (_, args) => rendered.Append(args.Output);
        var sent = await input.SendInputAsync(
            [new RemoteInputEvent { Kind = RemoteInputKind.Text, Text = "git rev-parse --short HEAD" },
                new RemoteInputEvent { Kind = RemoteInputKind.KeyDown, Key = "Enter" },
                new RemoteInputEvent { Kind = RemoteInputKind.KeyUp, Key = "Enter" }],
            "retro-ide-tests").ConfigureAwait(true);
        if (!sent.Succeeded)
            throw new InvalidOperationException(sent.Message);
        while (!rendered.ToString().Contains(head, StringComparison.Ordinal))
            await Task.Delay(20, token).ConfigureAwait(true);

        session.Layout.Factory.CloseDockable(document);
        await view.Terminal.WaitForExitAsync(token).ConfigureAwait(true);
        if (session.Documents.FindTerminal(view.ViewKey) is not null)
            throw new InvalidOperationException("The closed terminal document retained its registration.");
        Console.WriteLine("RemoteControl opened the repository terminal, sent input to its native shell, received Git output in the terminal control, and closed the session.");
    }
}
