using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RetroIde.Application;
using RetroIde.Application.Workspaces;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

public sealed partial class Phase02EditorSaveContractTests
{
    /// <summary>Runs the proven save assertions through the registered document controller and staged file save.</summary>
    /// <remarks>TEST-RETRO-007#ac-test-controller-ownership; TEST-RETRO-011#ac-test-save-source</remarks>
    [AvaloniaTheory]
    [InlineData("later-edit")]
    [InlineData("failed-write")]
    [InlineData("closed-view")]
    public async Task Save_contract_workbench(string scenario)
    {
        await using var live = new WorkbenchEditorPort();
        await VerifySaveScenarioAsync(live, scenario);
    }

    private sealed class WorkbenchEditorPort : IEditorPort
    {
        private sealed record WorkspaceContext(Guid WorkspaceId) : IWorkbenchWorkspaceContext;

        private sealed record Tab(string Source, DocumentViewModel View)
        {
            public List<SaveDelivery> Deliveries { get; } = [];
        }

        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("retro-ide-save-contract-");
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(16));
        private readonly TaskCompletionSource<DocumentSaveSnapshot> _capture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, Tab> _tabs = new(StringComparer.Ordinal);
        private readonly WorkbenchSession _session;
        private Task? _save;
        private OperationResult? _lastSave;
        private Guid? _requestId;
        private int _stageCount;

        public WorkbenchEditorPort()
        {
            _session = new WorkbenchSession(new WorkspaceContext(Guid.NewGuid()), savedLayout: null, stageSave: StageAsync);
        }

        public async Task OpenAsync(string name, string text)
        {
            var directory = _directory.CreateSubdirectory(name).FullName;
            var path = Path.Combine(directory, "main.asm");
            await File.WriteAllTextAsync(path, text, _timeout.Token);
            await _session.ExplorerView.OpenFolderCommand.ExecuteAsync(directory);
            var root = Assert.Single(_session.ExplorerView.Roots, candidate =>
                string.Equals(candidate.Path, directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            var context = _session.View.Address;
            var grant = new ActorGrant("editor-contract", context.WorkspaceId, context.ViewKey, canMutate: true);
            var result = await _session.Controller.DispatchAsync(_session.Registry.Explorer.OpenEntry,
                new ExplorerFeatures.OpenEntryInput(root.Id, "main.asm"), context, grant, Guid.NewGuid(), _timeout.Token);
            Assert.Equal("ok", result.Code);
            var view = Assert.IsType<DocumentViewModel>(_session.Layout.CurrentSource);
            Assert.True(_session.Documents.Owns(view.ViewKey, _session.View.ViewKey));
            var tab = new Tab(path, view);
            _tabs.Add(name, tab);
            view.CommandRequested += (_, request) =>
            {
                if (request.Payload is ExplorerFeatures.SaveDocumentInput)
                {
                    Assert.Null(_requestId);
                    Assert.Equal(view.ViewKey, request.OriginViewKey);
                    _requestId = request.RequestId;
                }
            };
            view.PropertyChanged += (_, change) =>
            {
                if (string.Equals(change.PropertyName, nameof(DocumentViewModel.LastResult), StringComparison.Ordinal) &&
                    view.LastResult is { } outcome && outcome.OperationId == _requestId)
                {
                    _lastSave = outcome;
                    tab.Deliveries.Add(new SaveDelivery(outcome.Code, Dispatcher.UIThread.CheckAccess()));
                }
            };
        }

        public Task ActivateAsync(string name)
        {
            var view = _tabs[name].View;
            var dockable = Assert.IsType<ShellDocument>(_session.Layout.Factory.FindDockable(_session.Layout.Root,
                candidate => string.Equals(candidate.Id, view.Scope, StringComparison.Ordinal)));
            _session.Layout.ActivateDocument(dockable);
            Assert.Same(view, _session.Layout.CurrentSource);
            return Task.CompletedTask;
        }

        public Task EditAsync(string name, string text)
        {
            _tabs[name].View.Buffer.Text = text;
            return Task.CompletedTask;
        }

        public async Task<CapturedSave> StartSaveAsync(string name)
        {
            var tab = _tabs[name];
            Assert.Same(tab.View, _session.Layout.CurrentSource);
            var contextAtRequest = tab.View.Capture();
            _save = tab.View.SaveCommand.ExecuteAsync(null);
            var capture = await _capture.Task.WaitAsync(_timeout.Token);
            Assert.Equal(contextAtRequest, capture.Context);
            Assert.Equal(tab.Source, Path.Combine(capture.Source.RootPath, capture.Source.Entry.RelativePath), ignoreCase: OperatingSystem.IsWindows());
            return new CapturedSave(Path.GetFileName(capture.Source.RootPath), capture.Text);
        }

        public async Task CloseAsync(string name)
        {
            var tab = _tabs[name];
            await tab.View.DiscardAndCloseCommand.ExecuteAsync(null);
            Assert.False(_session.Documents.Owns(tab.View.ViewKey, _session.View.ViewKey));
        }

        public async Task FinishSaveAsync(bool shouldFail)
        {
            _release.SetResult(shouldFail);
            await Assert.IsAssignableFrom<Task>(_save).WaitAsync(_timeout.Token);
            Assert.Equal(1, Volatile.Read(ref _stageCount));
            Assert.NotNull(_requestId);
            if (_lastSave is { Code: "ok" })
            {
                var snapshot = await _capture.Task;
                Assert.Equal(snapshot.Context.Revision, _tabs[Original].View.SavedRevision);
            }
        }

        public async Task<EditorObservation> InspectAsync(string name)
        {
            var tab = _tabs[name];
            var text = await File.ReadAllTextAsync(tab.Source, _timeout.Token);
            var live = _session.Documents.Owns(tab.View.ViewKey, _session.View.ViewKey);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(tab.Source)!, "*.tmp"));
            return new EditorObservation(text, live ? tab.View.Buffer.Text : null, live && tab.View.IsDirty, tab.Deliveries);
        }

        public async ValueTask DisposeAsync()
        {
            _release.TrySetResult(true);
            try
            {
                if (_save is not null)
                    await _save.WaitAsync(_timeout.Token);
            }
            finally
            {
                _session.Dispose();
                _timeout.Dispose();
                _directory.Delete(recursive: true);
            }
        }

        private async Task<PreparedDocumentSave> StageAsync(DocumentSaveSnapshot snapshot, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _stageCount);
            var staged = await PreparedDocumentSave.WriteAsync(snapshot, cancellation).ConfigureAwait(false);
            _capture.SetResult(snapshot);
            try
            {
                if (await _release.Task.WaitAsync(_timeout.Token).ConfigureAwait(false))
                    throw new IOException("The staged writer reported a fixture failure.");
                return staged;
            }
            catch
            {
                staged.Dispose();
                throw;
            }
        }
    }
}
