using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using Dock.Model.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using RetroIde.Application;
using RetroIde.Application.Workspaces;
using RetroIde.Mcp;
using RetroIde.Testing;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises the production workspace and editor bindings with source files.</summary>
public sealed class DocumentRevisionTests
{
    /// <summary>Editor activation uses stable root-relative identities and reuses the registered tab.</summary>
    /// <remarks>TEST-RETRO-012#ac-test-explorer-mixed-roots; TEST-RETRO-011#ac-test-buffer-state</remarks>
    [AvaloniaFact]
    public async Task Opens_same_name_sources_in_separate_registered_editor_tabs()
    {
        using var fixture = new DocumentFixture();
        var firstFile = fixture.Source("one", "first source");
        var secondFile = fixture.Source("two", "second source");
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var window = new WorkbenchWindow(session);
        window.Show();

        try
        {
            var first = await OpenThroughExplorerAsync(session, firstFile, timeout.Token, window: window);
            var firstEditor = await WaitForEditorAsync(window, first, timeout.Token);
            Assert.True(firstEditor.ShowLineNumbers);
            Assert.Equal("first source", firstEditor.Text);

            var second = await OpenThroughExplorerAsync(session, secondFile, timeout.Token, window, usePointer: true, project: true);
            var secondEditor = await WaitForEditorAsync(window, second, timeout.Token);
            Assert.NotEqual(first.ViewKey, second.ViewKey);
            Assert.NotEqual(first.DocumentId, second.DocumentId);
            Assert.NotEqual(first.Address.RootId, second.Address.RootId);
            Assert.Equal(first.Address.RelativePath, second.Address.RelativePath);
            Assert.Equal("second source", secondEditor.Text);
            Assert.NotSame(first.Buffer.UndoStack, second.Buffer.UndoStack);

            var firstNode = session.ExplorerView.TreeRoots.First().Children.Single();
            await session.ExplorerView.OpenEntryCommand.ExecuteAsync(firstNode);
            Assert.Same(first, session.Layout.CurrentSource);
            await WaitForEditorAsync(window, first, timeout.Token);
            var firstRoot = session.ExplorerView.TreeRoots.First();
            await session.ExplorerView.SetExpansionCommand.ExecuteAsync(new ExplorerViewModel.ExpansionIntent(firstRoot, false));
            Assert.False(firstRoot.IsExpanded);
            await WorkbenchRemoteControl.ClickAsync(window, "document.reveal.toolbar");
            await Assert.IsAssignableFrom<Task>(first.RevealCommand.ExecutionTask);
            while (session.ExplorerView.SelectedNode?.Address != first.Address)
                await Task.Delay(25, timeout.Token);
            Assert.True(firstRoot.IsExpanded);
            Assert.True(session.Layout.IsFocused("explorer"));

            var data = JsonSerializer.Serialize(session.Layout.Capture(), WireJson.Options);
            Assert.DoesNotContain(first.Scope, data, StringComparison.Ordinal);
            Assert.DoesNotContain(second.Scope, data, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    /// <summary>The editor activates the installed grammar for a C# file and leaves an unknown extension as plain text.</summary>
    /// <remarks>TEST-RETRO-011#ac-test-buffer-state</remarks>
    [AvaloniaFact]
    public async Task Supported_sources_get_syntax_highlighting_and_unknown_extensions_remain_plain_text()
    {
        using var fixture = new DocumentFixture();
        var csharp = fixture.Source("csharp", "class Sample {}", ".cs");
        var plain = fixture.Source("plain", "no configured grammar", ".fixturetext");
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            var code = await OpenThroughExplorerAsync(session, csharp, timeout.Token);
            var highlighted = await WaitForEditorAsync(window, code, timeout.Token);
            Assert.Contains(highlighted.TextArea.TextView.LineTransformers, IsTextMate);
            var text = await OpenThroughExplorerAsync(session, plain, timeout.Token);
            var unhighlighted = await WaitForEditorAsync(window, text, timeout.Token);
            Assert.DoesNotContain(unhighlighted.TextArea.TextView.LineTransformers, IsTextMate);
            Assert.True(unhighlighted.ShowLineNumbers);
            Assert.Equal("no configured grammar", unhighlighted.Text);
        }
        finally { window.Close(); }

        static bool IsTextMate(AvaloniaEdit.Rendering.IVisualLineTransformer transformer) =>
            transformer.GetType().Assembly == typeof(TextMate).Assembly;
    }

    /// <summary>MCP opens and saves the same registered document and rejects an outdated document revision.</summary>
    /// <remarks>TEST-RETRO-011#ac-test-save-source</remarks>
    [AvaloniaFact]
    public async Task Scoped_mcp_opens_and_saves_an_editor_revision()
    {
        using var fixture = new DocumentFixture();
        var source = fixture.Source("root", "original");
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        await session.ExplorerView.OpenFolderCommand.ExecuteAsync(Path.GetDirectoryName(source));
        var root = Assert.Single(session.ExplorerView.Roots);
        await using var host = await LoopbackMcpHost.StartAsync(session.Registry, session.Controller, session.View.Address, token);
        var attachment = host.CreateAttachment("editor-test", canMutate: true);
        using var http = attachment.CreateHttpClient();
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = attachment.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http), cancellationToken: token);

        var open = await client.CallToolAsync("workspace_open_file", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["context"] = session.View.Address, ["rootId"] = root.Id, ["relativePath"] = "main.asm",
        }, cancellationToken: token);
        var opened = open.StructuredContent!.Value;
        Assert.Equal("ok", opened.GetProperty("code").GetString());
        var view = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
        Assert.Equal(view.DocumentId, opened.GetProperty("data").GetProperty("documentId").GetGuid());
        var oldRevision = view.Capture();
        await VerifyContextContractAsync(client, oldRevision, token);
        var edit = SaveArguments(session, oldRevision);
        edit["offset"] = 0;
        edit["length"] = view.Buffer.TextLength;
        edit["text"] = "updated";
        var editResult = await client.CallToolAsync("editor_edit_text", edit, cancellationToken: token);
        Assert.Equal("ok", editResult.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.True(view.IsDirty);
        Assert.Equal("original", File.ReadAllText(source));

        var stale = await client.CallToolAsync("editor_save_document", SaveArguments(session, oldRevision), cancellationToken: token);
        Assert.Equal("document_changed", stale.StructuredContent!.Value.GetProperty("code").GetString());
        var saved = await client.CallToolAsync("editor_save_document", SaveArguments(session, view.Capture()), cancellationToken: token);
        Assert.Equal("ok", saved.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Equal("updated", File.ReadAllText(source));
        Assert.False(view.IsDirty);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, "*.tmp"));
    }

    /// <summary>A failed save retains its text, dirty status, and origin result.</summary>
    /// <remarks>TEST-RETRO-011#ac-test-save-source</remarks>
    [AvaloniaFact]
    public async Task Failed_save_retains_the_edit_buffer()
    {
        using var fixture = new DocumentFixture();
        var path = fixture.Source("root", "initial");
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var document = await OpenThroughExplorerAsync(session, path, timeout.Token);
        File.Delete(path);
        document.Buffer.Text = "unsaved";
        await document.SaveCommand.ExecuteAsync(null);
        Assert.Equal("save_failed", document.LastResult?.Code);
        Assert.Equal("unsaved", document.Buffer.Text);
        Assert.True(document.IsDirty);
        Assert.False(File.Exists(path));
    }

    /// <summary>A Save command keeps its captured target and routes completion to the live originating tab.</summary>
    /// <remarks>TEST-RETRO-007#ac-test-controller-ownership; TEST-RETRO-011#ac-test-save-source</remarks>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_save_keeps_its_target_and_respects_document_lifetime(bool closeDocument)
    {
        using var fixture = new DocumentFixture();
        var originalPath = fixture.Source("north", "previous");
        var otherPath = fixture.Source("south", "different");
        var prepared = new TaskCompletionSource<DocumentSaveSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stageCount = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var session = new WorkbenchSession(new TestWorkspace(Guid.NewGuid()), savedLayout: null, stageSave: StageAsync);
        var token = timeout.Token;
        var original = await OpenThroughExplorerAsync(session, originalPath, token);
        original.Buffer.Text = "captured";
        var expected = original.Capture();
        var requests = new List<ViewCommandRequested>();
        var completions = 0;
        original.CommandRequested += (_, request) =>
        {
            if (request.Payload is ExplorerFeatures.SaveDocumentInput)
                requests.Add(request);
        };
        original.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(DocumentViewModel.LastResult), StringComparison.Ordinal) &&
                requests.Count == 1 && original.LastResult?.OperationId == requests[0].RequestId)
            {
                Assert.True(Dispatcher.UIThread.CheckAccess());
                completions++;
            }
        };

        var result = original.SaveCommand.ExecuteAsync(null);
        try
        {
            var captured = await prepared.Task.WaitAsync(token);
            Assert.Equal(expected, captured.Context);
            Assert.Equal("captured", captured.Text);
            var request = Assert.Single(requests);
            Assert.Equal(original.ViewKey, request.OriginViewKey);
            Assert.True(session.Documents.Owns(original.ViewKey, session.View.ViewKey));
            var other = await OpenThroughExplorerAsync(session, otherPath, token);
            Assert.True(session.Documents.Owns(other.ViewKey, session.View.ViewKey));
            Assert.Same(other, session.Layout.CurrentSource);

            if (closeDocument)
            {
                await original.CloseCommand.ExecuteAsync(null);
                Assert.Equal("unsaved", original.LastResult?.Code);
                await original.DiscardAndCloseCommand.ExecuteAsync(null);
                Assert.False(session.Documents.Owns(original.ViewKey, session.View.ViewKey));
                release.SetResult();
                await result.WaitAsync(token);
                Assert.Equal(0, completions);
                Assert.Equal("unsaved", original.LastResult?.Code);
                Assert.Equal("previous", File.ReadAllText(originalPath));
            }
            else
            {
                original.Buffer.Text = "later edit";
                release.SetResult();
                await result.WaitAsync(token);
                Assert.Equal(1, completions);
                Assert.Equal(request.RequestId, original.LastResult?.OperationId);
                Assert.Equal("ok", original.LastResult?.Code);
                Assert.Equal("captured", File.ReadAllText(originalPath));
                Assert.Equal(expected.Revision, original.SavedRevision);
                Assert.True(original.IsDirty);
                Assert.Equal("later edit", original.Buffer.Text);
            }

            Assert.Equal(1, Volatile.Read(ref stageCount));
            Assert.Null(other.LastResult);
            Assert.Equal("different", File.ReadAllText(otherPath));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(originalPath)!, "*.tmp"));
        }
        finally { release.TrySetResult(); }

        async Task<PreparedDocumentSave> StageAsync(DocumentSaveSnapshot capture, CancellationToken cancellation)
        {
            Interlocked.Increment(ref stageCount);
            var staged = await PreparedDocumentSave.WriteAsync(capture, cancellation).ConfigureAwait(false);
            prepared.TrySetResult(capture);
            try { await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch { staged.Dispose(); throw; }
            return staged;
        }
    }

    /// <summary>Unexpected handler errors preserve the draft and can be retried from the owning view.</summary>
    [AvaloniaFact]
    public async Task Unexpected_save_failure_logs_the_exception_and_allows_an_explicit_retry()
    {
        using var fixture = new DocumentFixture();
        var path = fixture.Source("runtime-recovery", "on disk");
        var appData = Path.Combine(Path.GetDirectoryName(path)!, ".diagnostics");
        using var logs = LoggerFactory.Create(builder => builder.AddProvider(new DesktopErrorLog(appData)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var attempts = 0;
        using var session = new WorkbenchSession(new TestWorkspace(Guid.NewGuid()), savedLayout: null, stageSave: StageAsync, logs: logs);
        var document = await OpenThroughExplorerAsync(session, path, timeout.Token);
        document.Buffer.Text = "retained draft";

        await document.SaveCommand.ExecuteAsync(null);
        var failure = Assert.IsType<OperationResult>(document.LastResult);
        Assert.Equal("internal_error", failure.Code);
        Assert.Equal("on disk", File.ReadAllText(path));
        Assert.True(document.IsDirty);
        var log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(appData, "logs"), "*.log")));
        Assert.Contains(failure.OperationId.ToString(), log, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: injected save failure", log, StringComparison.Ordinal);
        Assert.DoesNotContain("injected save failure", failure.Summary, StringComparison.Ordinal);
        Assert.Equal(1, attempts);

        await document.SaveCommand.ExecuteAsync(null);
        Assert.Equal("ok", document.LastResult?.Code);
        Assert.Equal("retained draft", File.ReadAllText(path));
        Assert.False(document.IsDirty);

        async Task<PreparedDocumentSave> StageAsync(DocumentSaveSnapshot snapshot, CancellationToken token)
        {
            await Task.Yield();
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("injected save failure");
            return await PreparedDocumentSave.WriteAsync(snapshot, token).ConfigureAwait(false);
        }
    }

    /// <summary>The active source control retains its buffer and caret state across floating and redock.</summary>
    /// <remarks>TEST-RETRO-011#ac-test-buffer-state</remarks>
    [AvaloniaFact]
    public async Task Float_and_redock_preserve_document_undo_and_selection()
    {
        using var fixture = new DocumentFixture();
        var path = fixture.Source("root", "abcdef");
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        var window = new WorkbenchWindow(session);
        window.Show();
        DocumentViewModel? document = null;
        try
        {
            document = await OpenThroughExplorerAsync(session, path, token);
            var editor = await WaitForEditorAsync(window, document, token);
            editor.Document.Insert(editor.Document.TextLength, "!");
            editor.Select(1, 3);
            editor.CaretOffset = 4;

            var dock = session.Layout.Factory.FindDockable(session.Layout.Root,
                candidate => string.Equals(candidate.Id, document.Scope, StringComparison.Ordinal));
            Assert.NotNull(dock);
            var owner = Assert.IsAssignableFrom<IDock>(dock.Owner);
            var anchor = owner.VisibleDockables!.First(candidate => !ReferenceEquals(candidate, dock));
            session.Layout.Factory.FloatDockable(dock);
            var floating = Assert.IsAssignableFrom<Window>(Assert.Single(session.Layout.Root.Windows!).Host);
            var floated = await WaitForEditorAsync(floating, document, token);
            Assert.Same(editor.Document, floated.Document);
            Assert.Equal((4, 1, 3), (floated.CaretOffset, floated.SelectionStart, floated.SelectionLength));

            session.Layout.Factory.MoveDockable(Assert.IsAssignableFrom<IDock>(dock.Owner), owner, dock, anchor);
            session.Layout.Factory.SetActiveDockable(dock);
            var redocked = await WaitForEditorAsync(window, document, token);
            Assert.Equal((4, 1, 3), (redocked.CaretOffset, redocked.SelectionStart, redocked.SelectionLength));
            redocked.Document.UndoStack.Undo();
            Assert.Equal("abcdef", redocked.Text);
            Assert.False(document.IsDirty);
        }
        finally
        {
            if (document?.IsDirty == true)
                await document.SaveCommand.ExecuteAsync(null);
            window.Close();
        }
    }

    /// <summary>Rendered find/replace commands use the selected document and undo one replace-all as a unit.</summary>
    /// <remarks>TEST-RETRO-011#ac-test-buffer-state</remarks>
    [AvaloniaFact]
    public async Task Find_replace_and_history_preserve_separate_editor_buffers()
    {
        using var fixture = new DocumentFixture();
        var firstPath = fixture.Source("one", "Alpha alpha alpha");
        var secondPath = fixture.Source("two", "other alpha");
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            var other = await OpenThroughExplorerAsync(session, secondPath, token);
            other.Buffer.Insert(other.Buffer.TextLength, "!");
            var first = await OpenThroughExplorerAsync(session, firstPath, token);
            var editor = await WaitForEditorAsync(window, first, token);
            await WorkbenchRemoteControl.ClickAsync(window, "document.search.toolbar");
            FindEditorControl<TextBox>(window, "document.find.query").Text = "alpha";
            FindEditorControl<TextBox>(window, "document.find.replacement").Text = "beta";
            await WorkbenchRemoteControl.ClickAsync(window, "document.next.toolbar");
            await Assert.IsAssignableFrom<Task>(first.FindCommand.ExecutionTask);
            Assert.Equal((0, 5), (editor.SelectionStart, editor.SelectionLength));
            await first.FindCommand.ExecuteAsync(null);
            Assert.Equal((6, 5), (editor.SelectionStart, editor.SelectionLength));
            await first.FindPreviousCommand.ExecuteAsync(null);
            Assert.Equal((0, 5), (editor.SelectionStart, editor.SelectionLength));

            await WorkbenchRemoteControl.ClickAsync(window, "document.replace-all.toolbar");
            await Assert.IsAssignableFrom<Task>(first.ReplaceAllCommand.ExecutionTask);
            Assert.Equal("beta beta beta", editor.Text);
            Assert.Equal("other alpha!", other.Buffer.Text);
            await first.UndoCommand.ExecuteAsync(null);
            Assert.Equal("Alpha alpha alpha", editor.Text);
            Assert.False(first.IsDirty);
            await other.UndoCommand.ExecuteAsync(null);
            Assert.Equal("other alpha", other.Buffer.Text);
            await first.RedoCommand.ExecuteAsync(null);
            Assert.Equal("beta beta beta", editor.Text);
            await first.UndoCommand.ExecuteAsync(null);
        }
        finally { window.Close(); }
    }

    /// <summary>A source diagnostic opens its root-relative file and places the live editor at its line and column.</summary>
    /// <remarks>TEST-RETRO-011#ac-test-save-source</remarks>
    [AvaloniaFact]
    public async Task Diagnostic_location_opens_the_registered_editor_at_line_and_column()
    {
        using var fixture = new DocumentFixture();
        var source = fixture.Source("diagnostic/src", "first\nsecond line\nlast");
        var rootPath = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            await session.ExplorerView.OpenProjectCommand.ExecuteAsync(rootPath);
            var root = Assert.Single(session.ExplorerView.Roots);
            var diagnostic = new ExplorerFeatures.OpenEntryInput(root.Id, Path.Combine("src", "main.asm"), new SourcePosition(2, 4));
            await session.ExplorerView.OpenLocationCommand.ExecuteAsync(diagnostic);
            var tab = Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
            var editor = await WaitForEditorAsync(window, tab, token);
            Assert.Equal((2, 4), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column));
            Assert.Equal(diagnostic.RelativePath, tab.RelativePath);

            await session.ExplorerView.OpenLocationCommand.ExecuteAsync(diagnostic with { Position = new(0, 0) });
            Assert.Equal("invalid_location", session.ExplorerView.LastResult?.Code);
            Assert.Same(tab, session.Layout.CurrentSource);
            Assert.Equal((2, 4), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column));
        }
        finally { window.Close(); }
    }

    /// <summary>Dirty tabs stay open until the user explicitly chooses discard.</summary>
    [AvaloniaFact]
    public async Task Close_routes_through_the_owner_and_preserves_unsaved_text_until_discard()
    {
        using var fixture = new DocumentFixture();
        var source = fixture.Source("root", "saved");
        using var session = new WorkbenchSession(Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var token = timeout.Token;
        var window = new WorkbenchWindow(session);
        window.Show();
        try
        {
            var document = await OpenThroughExplorerAsync(session, source, token);
            await WaitForEditorAsync(window, document, token);
            var dockable = session.Layout.Factory.FindDockable(session.Layout.Root,
                item => string.Equals(item.Id, document.Scope, StringComparison.Ordinal));
            Assert.NotNull(dockable);
            document.Buffer.Text = "pending";
            session.Layout.Factory.CloseDockable(dockable);
            while (document.LastResult?.Code != "unsaved")
                await Task.Delay(25, token);
            Assert.Same(document, session.Layout.CurrentSource);
            Assert.Equal("pending", document.Buffer.Text);
            await WorkbenchRemoteControl.ClickAsync(window, "document.discard.toolbar");
            await Assert.IsAssignableFrom<Task>(document.DiscardAndCloseCommand.ExecutionTask);
            Assert.Null(session.Layout.CurrentSource);
            Assert.False(session.Documents.HasDocument(document.Address));
            Assert.Equal("unsaved", document.LastResult?.Code);
            Assert.Equal("saved", File.ReadAllText(source));
        }
        finally { window.Close(); }
    }

    private static async Task VerifyContextContractAsync(McpClient client, DocumentContext context, CancellationToken token)
    {
        var tools = await client.ListToolsAsync(cancellationToken: token);
        var readTool = tools.Single(tool => string.Equals(tool.Name, "editor_get_document", StringComparison.Ordinal));
        var editTool = tools.Single(tool => string.Equals(tool.Name, "editor_edit_text", StringComparison.Ordinal));
        var schema = readTool.ProtocolTool.OutputSchema!.Value.GetProperty("properties").GetProperty("data");
        Assert.Equal(schema.GetRawText(), editTool.JsonSchema.GetProperty("properties").GetProperty("document").GetRawText());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var read = await client.CallToolAsync("editor_get_document", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["viewKey"] = context.ViewKey,
        }, cancellationToken: token);
        var live = read.StructuredContent!.Value.GetProperty("data");
        Assert.Equal(context, live.Deserialize<DocumentContext>(WireJson.Options));
        Assert.Equal(schema.GetProperty("required").EnumerateArray().Select(field => field.GetString()).Order(StringComparer.Ordinal),
            live.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static T FindEditorControl<T>(Window window, string id) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), id, StringComparison.Ordinal));

    private static Dictionary<string, object?> SaveArguments(WorkbenchSession session, DocumentContext document) =>
        new(StringComparer.Ordinal) { ["context"] = session.View.Address, ["document"] = document };

    private static async Task<DocumentViewModel> OpenThroughExplorerAsync(WorkbenchSession session, string path,
        CancellationToken token, WorkbenchWindow? window = null, bool usePointer = false, bool project = false)
    {
        var openRoot = project ? session.ExplorerView.OpenProjectCommand : session.ExplorerView.OpenFolderCommand;
        await openRoot.ExecuteAsync(Path.GetDirectoryName(path));
        var root = session.ExplorerView.TreeRoots.Last();
        await session.ExplorerView.SetExpansionCommand.ExecuteAsync(new ExplorerViewModel.ExpansionIntent(root, true));
        while (root.Children.Count == 0 || root.Children[0].Address is null)
            await Task.Delay(25, token);

        var file = Assert.Single(root.Children);
        if (window is null)
            await session.ExplorerView.OpenEntryCommand.ExecuteAsync(file);
        else
            await OpenWithTreeGestureAsync(window, session, file, usePointer, token);
        return Assert.IsType<DocumentViewModel>(session.Layout.CurrentSource);
    }

    private static async Task OpenWithTreeGestureAsync(WorkbenchWindow window, WorkbenchSession session, ExplorerTreeNode file,
        bool usePointer, CancellationToken token)
    {
        TreeView? tree;
        do
        {
            window.UpdateLayout();
            tree = window.GetVisualDescendants().OfType<TreeView>().SingleOrDefault(candidate =>
                ReferenceEquals(candidate.ItemsSource, session.ExplorerView.TreeRoots));
            if (tree is null)
                await Task.Delay(25, token);
        } while (tree is null);
        window.UpdateLayout();
        var item = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(candidate => ReferenceEquals(candidate.DataContext, file));
        if (usePointer)
        {
            Assert.True(session.Layout.Focus("explorer"));
            item.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var label = item.GetVisualDescendants().OfType<TextBlock>().Single(block =>
                string.Equals(block.Text, file.Label, StringComparison.Ordinal));
            var point = Assert.IsType<Point>(label.TranslatePoint(new Point(Math.Min(8, label.Bounds.Width / 2), label.Bounds.Height / 2), window));
            var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
            Assert.True(ReferenceEquals(item, hit as TreeViewItem ?? hit.FindAncestorOfType<TreeViewItem>()),
                $"The tree row at {point} hit {hit.GetType().Name}. Tree {tree.Bounds}, row {item.Bounds}, label {label.Bounds}. Visible {label.IsEffectivelyVisible}.");
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
        }
        else
        {
            tree.SetCurrentValue(TreeView.SelectedItemProperty, file);
            while (!ReferenceEquals(file, session.ExplorerView.SelectedNode))
                await Task.Delay(25, token);
            Assert.True(item.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }
        while (session.Layout.CurrentSource?.Address != file.Address)
            await Task.Delay(25, token);
    }

    private static async Task<TextEditor> WaitForEditorAsync(Window window, DocumentViewModel document, CancellationToken token)
    {
        while (true)
        {
            window.UpdateLayout();
            var found = window.GetVisualDescendants().OfType<TextEditor>().SingleOrDefault(editor =>
                ReferenceEquals(editor.Document, document.Buffer) &&
                string.Equals(AutomationProperties.GetAutomationId(editor), "document.source-editor", StringComparison.Ordinal));
            if (found is not null)
                return found;
            await Task.Delay(25, token);
        }
    }

    private sealed record TestWorkspace(Guid WorkspaceId) : IWorkbenchWorkspaceContext;

    private sealed class DocumentFixture : IDisposable
    {
        private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("retro-ide-document-");

        public string Source(string folder, string content, string extension = ".asm")
        {
            var directory = Directory.CreateDirectory(Path.Combine(_temp.FullName, folder)).FullName;
            var file = Path.Combine(directory, "main" + extension);
            File.WriteAllText(file, content);
            return file;
        }

        public void Dispose() => _temp.Delete(recursive: true);
    }
}
