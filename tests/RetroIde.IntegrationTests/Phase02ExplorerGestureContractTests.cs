using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Proves keyboard and pointer source opening and exact Reveal in a mixed-root workspace.</summary>
/// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-mixed-roots. Each input route shares the root and tab identity assertions.</remarks>
public sealed partial class Phase02ExplorerGestureContractTests
{
    private const string FileName = "shared.asm";

    /// <summary>Checks the valid activation sequence and detects four independent navigation faults.</summary>
    [Theory]
    [InlineData("wrong-keyboard-file")]
    [InlineData("wrong-pointer-root")]
    [InlineData("duplicates-tab")]
    [InlineData("reveal-keeps-old-selection")]
    public async Task Reference_detects_activation_and_reveal_faults(string fault)
    {
        using var files = new GestureFiles();
        await VerifyGesturesAsync(new ReferenceGestures(files), files);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyGesturesAsync(new ReferenceGestures(files, fault), files));
    }

    private static async Task VerifyGesturesAsync(IGestures explorer, GestureFiles files)
    {
        var folder = await explorer.AddRootAsync(files.Folder, "folder");
        var project = await explorer.AddRootAsync(files.Project, "project");
        Assert.NotEqual(Guid.Empty, folder);
        Assert.NotEqual(Guid.Empty, project);
        Assert.NotEqual(folder, project);
        var folderFile = new SelectedFile(folder, FileName);
        var projectFile = new SelectedFile(project, FileName);

        var keyboard = await explorer.ActivateAsync(folderFile, Activation.Keyboard);
        Assert.NotEqual(Guid.Empty, keyboard.View);
        Assert.Equal(folderFile, keyboard.File);
        Assert.Equal("lda #1", keyboard.Buffer);
        var pointer = await explorer.ActivateAsync(projectFile, Activation.Pointer);
        Assert.NotEqual(Guid.Empty, pointer.View);
        Assert.Equal(projectFile, pointer.File);
        Assert.Equal("lda #2", pointer.Buffer);
        Assert.NotEqual(keyboard.View, pointer.View);
        Assert.Equal(keyboard, await explorer.ActivateAsync(folderFile, Activation.Keyboard));
        Assert.Equal(pointer, await explorer.ActivateAsync(projectFile, Activation.Pointer));
        Assert.Equal(keyboard, await explorer.ActivateAsync(folderFile, Activation.Keyboard));

        await explorer.SelectAsync(projectFile);
        Assert.Equal(projectFile, await explorer.SelectedAsync());
        Assert.Equal(folderFile, await explorer.RevealAsync(keyboard.View));
        Assert.Equal(folderFile, await explorer.SelectedAsync());
    }

    private interface IGestures
    {
        Task<Guid> AddRootAsync(string path, string kind);
        Task<OpenedSource> ActivateAsync(SelectedFile file, Activation activation);
        Task SelectAsync(SelectedFile file);
        Task<SelectedFile> SelectedAsync();
        Task<SelectedFile> RevealAsync(Guid tab);
    }

    private enum Activation { Keyboard, Pointer }
    private sealed record SelectedFile(Guid Root, string Path);
    private sealed record OpenedSource(Guid View, SelectedFile File, string Buffer);

    private sealed class GestureFiles : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("retro-ide-explorer-gestures-");

        public GestureFiles()
        {
            Folder = _root.CreateSubdirectory("folder").FullName;
            Project = _root.CreateSubdirectory("project").FullName;
            File.WriteAllText(Path.Combine(Folder, FileName), "lda #1");
            File.WriteAllText(Path.Combine(Project, FileName), "lda #2");
            File.WriteAllText(Path.Combine(Folder, "other.asm"), "rts");
            File.WriteAllText(Path.Combine(Project, "other.asm"), "nop");
        }

        public string Folder { get; }
        public string Project { get; }

        public void Dispose() => _root.Delete(recursive: true);
    }

    private sealed class ReferenceGestures(GestureFiles files, string? fault = null) : IGestures
    {
        private readonly Dictionary<Guid, string> _roots = [];
        private readonly Dictionary<SelectedFile, OpenedSource> _tabs = [];
        private Guid _folder;
        private SelectedFile? _selected;

        public Task<Guid> AddRootAsync(string path, string kind)
        {
            var id = Guid.NewGuid();
            var isFolder = string.Equals(path, files.Folder, StringComparison.Ordinal);
            Assert.Equal(isFolder ? "folder" : "project", kind);
            if (isFolder)
                _folder = id;
            _roots.Add(id, path);
            return Task.FromResult(id);
        }

        public async Task<OpenedSource> ActivateAsync(SelectedFile file, Activation activation)
        {
            _selected = file;
            var destination = activation switch
            {
                Activation.Keyboard when Fault("wrong-keyboard-file") => file with { Path = "other.asm" },
                Activation.Pointer when Fault("wrong-pointer-root") => file with { Root = _folder },
                _ => file,
            };
            if (!Fault("duplicates-tab") && _tabs.TryGetValue(destination, out var current))
                return current;

            var text = await File.ReadAllTextAsync(Path.Combine(_roots[destination.Root], destination.Path), TestContext.Current.CancellationToken);
            var source = new OpenedSource(Guid.NewGuid(), destination, text);
            _tabs[destination] = source;
            return source;
        }

        public Task SelectAsync(SelectedFile file)
        {
            _selected = file;
            return Task.CompletedTask;
        }

        public Task<SelectedFile> SelectedAsync() => Task.FromResult(Assert.IsType<SelectedFile>(_selected));

        public Task<SelectedFile> RevealAsync(Guid tab)
        {
            if (!Fault("reveal-keeps-old-selection"))
                _selected = _tabs.Values.Single(opened => opened.View == tab).File;
            return SelectedAsync();
        }

        private bool Fault(string candidate) => string.Equals(fault, candidate, StringComparison.Ordinal);
    }
}
