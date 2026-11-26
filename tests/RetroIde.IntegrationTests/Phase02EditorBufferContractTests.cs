using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Exercises independent source documents, rendered tab state, and one-step editor history.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-011#ac-test-buffer-state. Share the same observations across the reference and workbench ports.</remarks>
public sealed partial class Phase02EditorBufferContractTests
{
    private const string Source = "// alpha alpha\nclass Sample {}\n";
    private const string ReplacedSource = "// beta beta\nclass Sample {}\n";
    private const string Plain = "plain alpha";

    /// <summary>Validates the workflow and checks that the assertions detect each injected buffer fault.</summary>
    [Theory]
    [InlineData("shared-buffer")]
    [InlineData("lost-caret")]
    [InlineData("lost-selection")]
    [InlineData("lost-undo")]
    [InlineData("wrong-search-match")]
    [InlineData("first-replacement-only")]
    [InlineData("no-redo")]
    [InlineData("highlight-unknown")]
    [InlineData("missing-syntax")]
    public async Task Reference_editor_rejects_each_buffer_fault(string fault)
    {
        using var files = new BufferFixture();
        await VerifyBufferStateAsync(new ReferenceBufferPort(files), files);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyBufferStateAsync(new ReferenceBufferPort(files, fault), files));
    }

    private static async Task VerifyBufferStateAsync(IBufferPort editor, BufferFixture files)
    {
        var first = await editor.OpenAsync(files.CodeName);
        var second = await editor.OpenAsync(files.PlainName);
        var plain = await editor.ObserveAsync(second);
        Assert.Equal(Plain, plain.Text);
        Assert.False(plain.HasGrammar);
        await editor.AppendAsync(second, "!");
        var modifiedPlain = await editor.ObserveAsync(second);
        Assert.Equal(Plain + "!", modifiedPlain.Text);

        var code = await editor.ObserveAsync(first);
        Assert.Equal(Source, code.Text);
        Assert.True(code.HasGrammar);
        Assert.NotSame(code.Buffer, plain.Buffer);
        await editor.SelectAsync(first, caret: 0, start: 0, length: 0);
        await editor.FindAsync(first, "alpha");
        var found = await editor.ObserveAsync(first);
        Assert.Equal((3, 5), (found.Start, found.Length));

        await editor.ReplaceAllAsync(first, "alpha", "beta");
        await editor.SelectAsync(first, caret: 7, start: 3, length: 4);
        var edited = await editor.ObserveAsync(first);
        Assert.Equal(ReplacedSource, edited.Text);
        Assert.Equal((7, 3, 4), (edited.Caret, edited.Start, edited.Length));
        Assert.False(edited.Floating);
        Assert.True(edited.CanUndo);

        await editor.FloatAsync(first);
        Assert.Equal(edited with { Floating = true }, await editor.ObserveAsync(first));
        await editor.RedockAsync(first);
        Assert.Equal(edited, await editor.ObserveAsync(first));
        await editor.UndoAsync(first);
        Assert.Equal(Source, (await editor.ObserveAsync(first)).Text);
        Assert.Equal(Plain + "!", (await editor.ObserveAsync(second)).Text);
        await editor.RedoAsync(first);
        Assert.Equal(ReplacedSource, (await editor.ObserveAsync(first)).Text);
        await editor.UndoAsync(second);
        Assert.Equal(Plain, (await editor.ObserveAsync(second)).Text);
        Assert.Equal(ReplacedSource, (await editor.ObserveAsync(first)).Text);
    }

    private interface IBufferPort
    {
        Task<Guid> OpenAsync(string relativePath);
        Task AppendAsync(Guid tab, string text);
        Task SelectAsync(Guid tab, int caret, int start, int length);
        Task FindAsync(Guid tab, string query);
        Task ReplaceAllAsync(Guid tab, string query, string replacement);
        Task FloatAsync(Guid tab);
        Task RedockAsync(Guid tab);
        Task UndoAsync(Guid tab);
        Task RedoAsync(Guid tab);
        Task<BufferState> ObserveAsync(Guid tab);
    }

    private sealed record BufferState(object Buffer, string Text, int Caret, int Start, int Length, bool HasGrammar, bool Floating, bool CanUndo);

    private sealed class BufferFixture : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("retro-ide-buffer-contract-");

        public BufferFixture()
        {
            File.WriteAllText(Path.Combine(Root, CodeName), Source);
            File.WriteAllText(Path.Combine(Root, PlainName), Plain);
        }

        public string Root => _root.FullName;
        public string CodeName => "sample.cs";
        public string PlainName => "sample.fixturetext";

        public void Dispose() => _root.Delete(recursive: true);
    }

    private sealed class ReferenceBufferPort(BufferFixture files, string? fault = null) : IBufferPort
    {
        private sealed class Tab(string text, bool hasGrammar, object buffer)
        {
            public string Text { get; set; } = text;
            public bool Grammar { get; } = hasGrammar;
            public object Buffer { get; } = buffer;
            public int Caret { get; set; }
            public int Start { get; set; }
            public int Length { get; set; }
            public bool Floating { get; set; }
            public Stack<string> Undo { get; } = new();
            public Stack<string> Redo { get; } = new();

            public void Edit(string next)
            {
                Undo.Push(Text);
                Redo.Clear();
                Text = next;
            }
        }

        private readonly Dictionary<Guid, Tab> _tabs = [];
        private readonly object _shared = new();

        public async Task<Guid> OpenAsync(string relativePath)
        {
            var source = await File.ReadAllTextAsync(Path.Combine(files.Root, relativePath), TestContext.Current.CancellationToken);
            var code = string.Equals(relativePath, files.CodeName, StringComparison.Ordinal);
            var grammar = code ? !Fault("missing-syntax") : Fault("highlight-unknown");
            var key = Guid.NewGuid();
            _tabs.Add(key, new Tab(source, grammar, Fault("shared-buffer") ? _shared : new object()));
            return key;
        }

        public Task AppendAsync(Guid tab, string text)
        {
            var entry = _tabs[tab];
            entry.Edit(entry.Text + text);
            return Task.CompletedTask;
        }

        public Task SelectAsync(Guid tab, int caret, int start, int length)
        {
            var entry = _tabs[tab];
            entry.Caret = caret;
            entry.Start = start;
            entry.Length = length;
            return Task.CompletedTask;
        }

        public Task FindAsync(Guid tab, string query)
        {
            var entry = _tabs[tab];
            var match = Fault("wrong-search-match")
                ? entry.Text.LastIndexOf(query, StringComparison.Ordinal)
                : entry.Text.IndexOf(query, StringComparison.Ordinal);
            return SelectAsync(tab, match + query.Length, match, query.Length);
        }

        public Task ReplaceAllAsync(Guid tab, string query, string replacement)
        {
            var entry = _tabs[tab];
            var updated = Fault("first-replacement-only")
                ? entry.Text.Remove(entry.Text.IndexOf(query, StringComparison.Ordinal), query.Length)
                    .Insert(entry.Text.IndexOf(query, StringComparison.Ordinal), replacement)
                : entry.Text.Replace(query, replacement, StringComparison.Ordinal);
            entry.Edit(updated);
            return Task.CompletedTask;
        }

        public Task FloatAsync(Guid tab)
        {
            var entry = _tabs[tab];
            entry.Floating = true;
            if (Fault("lost-caret"))
                entry.Caret = 0;
            if (Fault("lost-undo"))
                entry.Undo.Clear();
            return Task.CompletedTask;
        }

        public Task RedockAsync(Guid tab)
        {
            var entry = _tabs[tab];
            entry.Floating = false;
            if (Fault("lost-selection"))
                entry.Length = 0;
            return Task.CompletedTask;
        }

        public Task UndoAsync(Guid tab)
        {
            var entry = _tabs[tab];
            entry.Redo.Push(entry.Text);
            entry.Text = entry.Undo.Pop();
            return Task.CompletedTask;
        }

        public Task RedoAsync(Guid tab)
        {
            var entry = _tabs[tab];
            if (!Fault("no-redo"))
            {
                entry.Undo.Push(entry.Text);
                entry.Text = entry.Redo.Pop();
            }
            return Task.CompletedTask;
        }

        public Task<BufferState> ObserveAsync(Guid tab)
        {
            var entry = _tabs[tab];
            return Task.FromResult(new BufferState(entry.Buffer, entry.Text, entry.Caret, entry.Start, entry.Length, entry.Grammar,
                entry.Floating, entry.Undo.Count > 0));
        }

        private bool Fault(string candidate) => string.Equals(fault, candidate, StringComparison.Ordinal);
    }
}
