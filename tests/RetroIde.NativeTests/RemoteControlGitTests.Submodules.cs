using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application.Git;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static partial class RemoteControlGitTests
{
    private static async Task VerifySubmodulesAsync(Window window, SourceControlViewModel view, GitWorktreeFixture sample, CancellationToken token)
    {
        const string ModulePath = "native deps/fixture library";
        const string UrlKey = "submodule.native.fixture.url";
        var checkout = Path.Combine(sample.Primary, ModulePath);
        await sample.GitAsync(sample.Primary, token, "-c", "protocol.file.allow=always", "submodule", "add", "--name",
            "native.fixture", "--", sample.Remote, ModulePath).ConfigureAwait(true);
        await sample.GitAsync(sample.Primary, token, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgSign=false", "commit", "--only", "-m", "Pin the native submodule", "--", ".gitmodules", ModulePath).ConfigureAwait(true);
        var pin = (await sample.GitAsync(checkout, token, "rev-parse", "HEAD").ConfigureAwait(true)).Trim();
        await sample.GitAsync(sample.Primary, token, "submodule", "deinit", "--", ModulePath).ConfigureAwait(true);

        const string open = "git.submodule.open.pane";
        (await FindAsync<Button>(window, open, token).ConfigureAwait(true)).BringIntoView();
        await WorkbenchRemoteControl.ClickAsync(window, open).ConfigureAwait(true);
        while (window.OwnedWindows.Count == 0)
            await Task.Delay(20, token).ConfigureAwait(true);
        var dialog = window.OwnedWindows.Single();
        var list = await FindAsync<ListBox>(dialog, "git.submodule.entries", token).ConfigureAwait(true);
        var entry = SelectEntry();
        if (entry.IsConfigured || entry.HasCheckout || !string.Equals(pin, entry.IndexCommit, StringComparison.Ordinal))
            throw new InvalidOperationException("Native Submodules did not show the deinitialized checkout and recorded commit.");
        await ClickAsync("git.submodule.init.toolbar").ConfigureAwait(true);
        entry = SelectEntry();
        if (!entry.IsConfigured || entry.HasCheckout)
            throw new InvalidOperationException("Initialize Submodule did not update the Git configuration state.");

        await ClickAsync("git.submodule.update.toolbar").ConfigureAwait(true);
        entry = SelectEntry();
        if (!entry.HasCheckout || !string.Equals(pin, entry.Head, StringComparison.Ordinal))
            throw new InvalidOperationException("Update Submodule did not check out the recorded commit.");
        await sample.GitAsync(sample.Primary, token, "config", "--file", ".gitmodules", UrlKey, sample.Peer).ConfigureAwait(true);
        await ClickAsync("git.submodule.list.toolbar").ConfigureAwait(true);
        _ = SelectEntry();
        await ClickAsync("git.submodule.sync.toolbar").ConfigureAwait(true);
        var url = (await sample.GitAsync(sample.Primary, token, "config", "--local", "--get", UrlKey).ConfigureAwait(true)).Trim();
        if (!string.Equals(url.Replace('/', Path.DirectorySeparatorChar), sample.Peer,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Sync Submodule did not apply the current metadata URL.");
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.submodule.close").ConfigureAwait(true);
        await (view.OpenSubmodulesCommand.ExecutionTask ?? throw new InvalidOperationException("Open Submodules did not dispatch."))
            .WaitAsync(token).ConfigureAwait(true);
        Console.WriteLine("RemoteControl listed, initialized, updated, and synced a Git submodule in the selected checkout.");

        GitSubmoduleState.Module SelectEntry()
        {
            var row = ((IEnumerable<GitSubmoduleState.Module>)list.ItemsSource!).Single(item =>
                string.Equals(item.Path, ModulePath, StringComparison.Ordinal));
            list.SelectedItem = row;
            return row;
        }

        async Task ClickAsync(string id)
        {
            var button = await FindAsync<Button>(dialog, id, token).ConfigureAwait(true);
            await WorkbenchRemoteControl.ClickAsync(dialog, id).ConfigureAwait(true);
            var command = (IAsyncRelayCommand)button.Command!;
            await (command.ExecutionTask ?? throw new InvalidOperationException("The submodule command did not dispatch."))
                .WaitAsync(token).ConfigureAwait(true);
            if (!string.Equals(view.LastResult?.Code, "ok", StringComparison.Ordinal))
                throw new InvalidOperationException($"Native submodule action {id} failed with {view.LastResult?.Code}.");
        }
    }
}
