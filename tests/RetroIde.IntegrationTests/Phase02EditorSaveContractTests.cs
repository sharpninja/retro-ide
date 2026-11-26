using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Drives a shared save contract against independent fixtures and the workbench.</summary>
public sealed partial class Phase02EditorSaveContractTests
{
    private const string Original = "north";
    private const string Other = "south";
    private const string Before = "previous contents";
    private const string Captured = "contents at save request";
    private const string Later = "contents after save request";
    private const string OtherText = "untouched second file";

    /// <summary>Confirms the shared assertions accept valid fixture behavior and reject specific faults.</summary>
    [Theory]
    [InlineData("later-edit", "wrong-file")]
    [InlineData("later-edit", "dirty-cleared")]
    [InlineData("later-edit", "wrong-recipient")]
    [InlineData("failed-write", "write-on-failure")]
    [InlineData("closed-view", "late-delivery")]
    public async Task Save_contract_mock(string scenario, string fault)
    {
        await using var correct = new ScriptedEditorPort();
        await VerifySaveScenarioAsync(correct, scenario);

        await using var incorrect = new ScriptedEditorPort(fault);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifySaveScenarioAsync(incorrect, scenario));
    }

    private static async Task VerifySaveScenarioAsync(IEditorPort editor, string scenario)
    {
        await editor.OpenAsync(Original, Before);
        await editor.OpenAsync(Other, OtherText);
        await editor.ActivateAsync(Original);
        await editor.EditAsync(Original, Captured);

        var snapshot = await editor.StartSaveAsync(Original);
        Assert.Equal(new CapturedSave(Original, Captured), snapshot);
        await editor.ActivateAsync(Other);
        if (string.Equals(scenario, "later-edit", StringComparison.Ordinal))
            await editor.EditAsync(Original, Later);
        if (string.Equals(scenario, "closed-view", StringComparison.Ordinal))
            await editor.CloseAsync(Original);

        await editor.FinishSaveAsync(shouldFail: string.Equals(scenario, "failed-write", StringComparison.Ordinal));
        var first = await editor.InspectAsync(Original);
        var second = await editor.InspectAsync(Other);

        Assert.Equal(OtherText, second.FileText);
        Assert.Equal(OtherText, second.BufferText);
        Assert.Empty(second.Deliveries);
        switch (scenario)
        {
            case "later-edit":
                Assert.Equal(Captured, first.FileText);
                Assert.Equal(Later, first.BufferText);
                Assert.True(first.Dirty);
                Assert.Equal(new SaveDelivery("ok", OnOwnerDispatcher: true), Assert.Single(first.Deliveries));
                break;
            case "failed-write":
                Assert.Equal(Before, first.FileText);
                Assert.Equal(Captured, first.BufferText);
                Assert.True(first.Dirty);
                Assert.Equal(new SaveDelivery("save_failed", OnOwnerDispatcher: true), Assert.Single(first.Deliveries));
                break;
            case "closed-view":
                Assert.Equal(Before, first.FileText);
                Assert.Null(first.BufferText);
                Assert.Empty(first.Deliveries);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private interface IEditorPort : IAsyncDisposable
    {
        Task OpenAsync(string name, string text);
        Task ActivateAsync(string name);
        Task EditAsync(string name, string text);
        Task<CapturedSave> StartSaveAsync(string name);
        Task CloseAsync(string name);
        Task FinishSaveAsync(bool shouldFail);
        Task<EditorObservation> InspectAsync(string name);
    }

    private sealed record CapturedSave(string File, string Text);
    private sealed record SaveDelivery(string Code, bool OnOwnerDispatcher);
    private sealed record EditorObservation(string FileText, string? BufferText, bool Dirty, IReadOnlyList<SaveDelivery> Deliveries);

    private sealed class ScriptedEditorPort(string? fault = null) : IEditorPort
    {
        private sealed class Tab(string text)
        {
            public string FileText = text;
            public string? BufferText = text;
            public bool Dirty;
            public List<SaveDelivery> Deliveries { get; } = [];
        }

        private readonly Dictionary<string, Tab> _tabs = new(StringComparer.Ordinal);
        private string? _active;
        private CapturedSave? _pending;

        public Task OpenAsync(string name, string text)
        {
            _tabs.Add(name, new Tab(text));
            return ActivateAsync(name);
        }

        public Task ActivateAsync(string name)
        {
            Assert.NotNull(_tabs[name].BufferText);
            _active = name;
            return Task.CompletedTask;
        }

        public Task EditAsync(string name, string text)
        {
            var tab = _tabs[name];
            Assert.NotNull(tab.BufferText);
            tab.BufferText = text;
            tab.Dirty = true;
            return Task.CompletedTask;
        }

        public Task<CapturedSave> StartSaveAsync(string name)
        {
            Assert.Equal(_active, name);
            Assert.Null(_pending);
            var text = _tabs[name].BufferText;
            Assert.NotNull(text);
            _pending = new CapturedSave(name, text);
            return Task.FromResult(_pending);
        }

        public Task CloseAsync(string name)
        {
            _tabs[name].BufferText = null;
            return Task.CompletedTask;
        }

        public Task FinishSaveAsync(bool shouldFail)
        {
            var snapshot = _pending;
            Assert.NotNull(snapshot);
            _pending = null;
            var origin = _tabs[snapshot.File];
            if (origin.BufferText is null && !string.Equals(fault, "late-delivery", StringComparison.Ordinal))
                return Task.CompletedTask;

            var destination = string.Equals(fault, "wrong-file", StringComparison.Ordinal) ? _tabs[_active!] : origin;
            if (!shouldFail || string.Equals(fault, "write-on-failure", StringComparison.Ordinal))
                destination.FileText = snapshot.Text;
            if (!shouldFail)
                origin.Dirty = string.Equals(fault, "dirty-cleared", StringComparison.Ordinal) is false && origin.BufferText != snapshot.Text;

            var recipient = string.Equals(fault, "wrong-recipient", StringComparison.Ordinal) ? _tabs[_active!] : origin;
            recipient.Deliveries.Add(new SaveDelivery(shouldFail ? "save_failed" : "ok", OnOwnerDispatcher: true));
            return Task.CompletedTask;
        }

        public Task<EditorObservation> InspectAsync(string name)
        {
            var tab = _tabs[name];
            return Task.FromResult(new EditorObservation(tab.FileText, tab.BufferText, tab.Dirty, tab.Deliveries));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
