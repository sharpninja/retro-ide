using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using ModelContextProtocol.Client;
using RetroIde.Application.Git;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;

namespace RetroIde.NativeTests;

internal static class RemoteControlGitTests
{
    /// <summary>Exercises the Source Control commands and staged diff in a native desktop without physical input.</summary>
    /// <remarks>TEST-RETRO-013#ac-test-git-stage-and-push; TEST-RETRO-015#ac-test-mcp-same-instance.</remarks>
    internal static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = timeout.Token;
        using var sample = await GitWorktreeFixture.CreateAsync("git", token).ConfigureAwait(true);
        const string Change = "native Source Control change";
        GitWorktreeFixture.Write(sample.Primary, GitWorktreeFixture.ChangedPath, Change + "\n");
        await using var session = new WorkbenchSession(Guid.NewGuid());
        var window = new WorkbenchWindow(session) { Width = 1280, Height = 900 };
        window.Show();
        try
        {
            var platform = window.TryGetPlatformHandle() ?? throw new InvalidOperationException("Git verification requires a native window.");
            await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token).ConfigureAwait(true);
            var attachment = host.CreateAttachment("native-git-workbench", canMutate: true);
            using var http = attachment.CreateHttpClient();
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
            {
                Endpoint = attachment.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
            }, http), cancellationToken: token).ConfigureAwait(true);

            var opened = await MutateAsync("workspace_open_root", new(StringComparer.Ordinal)
            {
                ["path"] = sample.Primary,
                ["kind"] = "folder",
            }).ConfigureAwait(true);
            _ = await MutateAsync("git_select_repository", new(StringComparer.Ordinal)
            {
                ["rootId"] = opened.GetProperty("data").GetProperty("id").GetGuid(),
                ["relativePath"] = "",
            }).ConfigureAwait(true);
            _ = await MutateAsync("workbench_focus_tool", new(StringComparer.Ordinal) { ["toolId"] = "source-control" }).ConfigureAwait(true);
            _ = await InvokeAsync("git_get_status").ConfigureAwait(true);

            var list = await FindAsync<ListBox>(window, "git.status.working", token).ConfigureAwait(true);
            list.SelectedItem = ((IEnumerable<GitStatusEntry>)list.ItemsSource!).Single(item =>
                string.Equals(item.Path, GitWorktreeFixture.ChangedPath, StringComparison.Ordinal));
            await WorkbenchRemoteControl.ClickAsync(window, "git.file.stage.pane").ConfigureAwait(true);
            await (session.SourceControl.StageFileCommand.ExecutionTask ??
                throw new InvalidOperationException("Source Control did not dispatch Stage File.")).WaitAsync(token).ConfigureAwait(true);
            _ = await InvokeAsync("git_get_status").ConfigureAwait(true);
            var staged = await FindAsync<ListBox>(window, "git.status.staged", token).ConfigureAwait(true);
            staged.SelectedItem = ((IEnumerable<GitStatusEntry>)staged.ItemsSource!).Single(item =>
                string.Equals(item.Path, GitWorktreeFixture.ChangedPath, StringComparison.Ordinal));
            await WorkbenchRemoteControl.ClickAsync(window, "git.diff.open.pane").ConfigureAwait(true);
            await (session.SourceControl.OpenDiffCommand.ExecutionTask ??
                throw new InvalidOperationException("Source Control did not dispatch Open Diff.")).WaitAsync(token).ConfigureAwait(true);
            var editor = await FindAsync<TextEditor>(window, "git.diff.patch", token).ConfigureAwait(true);
            if (!editor.IsReadOnly || !editor.Text.Contains("+" + Change, StringComparison.Ordinal))
                throw new InvalidOperationException("Source Control did not present the staged patch in a read-only native tab.");
            Console.WriteLine($"RemoteControl staged an exact Git path and opened its read-only diff on {platform.HandleDescriptor} without physical input.");

            async Task<JsonElement> MutateAsync(string tool, Dictionary<string, object?> arguments)
            {
                var current = await InvokeAsync("workbench_get_context").ConfigureAwait(true);
                arguments["context"] = current.GetProperty("context");
                return await InvokeAsync(tool, arguments).ConfigureAwait(true);
            }

            async Task<JsonElement> InvokeAsync(string tool, Dictionary<string, object?>? arguments = null)
            {
                var response = await client.CallToolAsync(tool, arguments, cancellationToken: token).ConfigureAwait(true);
                if (response.StructuredContent is not { } result ||
                    !string.Equals(result.GetProperty("code").GetString(), "ok", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Native Git tool {tool} failed.");
                return result;
            }
        }
        finally { window.Close(); }
    }

    private static async Task<T> FindAsync<T>(Window window, string id, CancellationToken token) where T : Control
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            window.UpdateLayout();
            var control = window.GetVisualDescendants().OfType<T>().SingleOrDefault(item =>
                item.IsEffectivelyVisible && string.Equals(AutomationProperties.GetAutomationId(item), id, StringComparison.Ordinal));
            if (control is not null)
                return control;
            await Task.Delay(20, token).ConfigureAwait(true);
        }
    }
}
