using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using RetroIde.Application.Git;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static partial class RemoteControlGitTests
{
    private static async Task VerifyLfsAsync(Window window, SourceControlViewModel view, GitWorktreeFixture sample, CancellationToken token)
    {
        const string Relative = "Café [native].rom";
        var installed = (await sample.RunAsync(sample.Primary, token, "lfs", "version").ConfigureAwait(true)).ExitCode == 0;
        var path = Path.Combine(sample.Primary, Relative);
        var contents = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var pointer = "";
        if (installed)
            pointer = await PrepareLfsFixtureAsync(sample, Relative, path, contents, token).ConfigureAwait(true);

        const string open = "git.lfs.open.pane";
        (await FindAsync<Button>(window, open, token).ConfigureAwait(true)).BringIntoView();
        await WorkbenchRemoteControl.ClickAsync(window, open).ConfigureAwait(true);
        while (window.OwnedWindows.Count == 0)
            await Task.Delay(20, token).ConfigureAwait(true);
        var dialog = window.OwnedWindows.Single();
        if (view.LfsStatus?.IsInstalled != installed)
            throw new InvalidOperationException("Git LFS reported the wrong native availability.");
        if (installed)
        {
            var files = await FindAsync<ListBox>(dialog, "git.lfs.files", token).ConfigureAwait(true);
            var entry = FindEntry(files);
            if (entry.IsDownloaded || entry.IsMaterialized)
                throw new InvalidOperationException("The native Git LFS dialog did not expose the missing object.");
            await ClickActionAsync("git.lfs.fetch.toolbar").ConfigureAwait(true);
            if (!FindEntry(files).IsDownloaded || !string.Equals(pointer, await File.ReadAllTextAsync(path, token).ConfigureAwait(true), StringComparison.Ordinal))
                throw new InvalidOperationException("Git LFS Fetch did not cache the object and preserve the pointer file.");
            await ClickActionAsync("git.lfs.checkout.toolbar").ConfigureAwait(true);
            var materialized = await File.ReadAllBytesAsync(path, token).ConfigureAwait(true);
            if (!FindEntry(files).IsMaterialized || !contents.AsSpan().SequenceEqual(materialized))
                throw new InvalidOperationException("Git LFS Checkout did not materialize the cached bytes.");
            Console.WriteLine("RemoteControl verified Git LFS status, fetched a native object, and checked out the exact binary contents.");
        }
        else
        {
            var status = await FindAsync<TextBlock>(dialog, "git.lfs.status", token).ConfigureAwait(true);
            if (!string.Equals(status.Text, view.Localization.Text(WorkbenchText.GitLfsUnavailable), StringComparison.Ordinal))
                throw new InvalidOperationException("The Git LFS dialog did not show its unavailable state.");
        }
        await WorkbenchRemoteControl.ClickAsync(dialog, "git.lfs.close").ConfigureAwait(true);
        await (view.OpenLfsCommand.ExecutionTask ?? throw new InvalidOperationException("Open Git LFS did not dispatch."))
            .WaitAsync(token).ConfigureAwait(true);

        static GitLfsState.FileEntry FindEntry(ListBox list) =>
            ((IEnumerable<GitLfsState.FileEntry>)list.ItemsSource!).Single(entry => string.Equals(entry.Path, Relative, StringComparison.Ordinal));

        async Task ClickActionAsync(string id)
        {
            var button = await FindAsync<Button>(dialog, id, token).ConfigureAwait(true);
            await WorkbenchRemoteControl.ClickAsync(dialog, id).ConfigureAwait(true);
            await (((IAsyncRelayCommand)button.Command!).ExecutionTask ?? throw new InvalidOperationException("Git LFS action did not dispatch."))
                .WaitAsync(token).ConfigureAwait(true);
            if (!string.Equals(view.LastResult?.Code, "ok", StringComparison.Ordinal))
                throw new InvalidOperationException($"Git LFS action {id} failed with {view.LastResult?.Code}.");
        }
    }

    private static async Task<string> PrepareLfsFixtureAsync(GitWorktreeFixture sample, string relative, string path, byte[] bytes, CancellationToken token)
    {
        var repo = sample.Primary;
        await sample.GitAsync(repo, token, "lfs", "install", "--local", "--skip-repo").ConfigureAwait(true);
        await sample.GitAsync(repo, token, "lfs", "track", "*.rom").ConfigureAwait(true);
        await File.WriteAllBytesAsync(path, bytes, token).ConfigureAwait(true);
        await sample.GitAsync(repo, token, "--literal-pathspecs", "add", "--", ".gitattributes", relative).ConfigureAwait(true);
        await sample.GitAsync(repo, token, "--literal-pathspecs", "commit", "--only", "-m", "Store native LFS media", "--",
            ".gitattributes", relative).ConfigureAwait(true);
        await sample.GitAsync(repo, token, "lfs", "push", "origin", "HEAD").ConfigureAwait(true);
        var pointer = await sample.GitAsync(repo, token, "show", "HEAD:" + relative).ConfigureAwait(true);
        var oid = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await File.WriteAllTextAsync(path, pointer, new UTF8Encoding(false), token).ConfigureAwait(true);
        File.Delete(Path.Combine(repo, ".git", "lfs", "objects", oid[..2], oid[2..4], oid));
        return pointer;
    }
}
