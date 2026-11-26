using Xunit;
using Xunit.Sdk;

namespace RetroIde.IntegrationTests;

/// <summary>Runs the same root and document routing assertions against a scripted reference and the workbench.</summary>
/// <remarks>
/// 2026-11-26 · TEST-RETRO-012#ac-test-explorer-mixed-roots.
/// Record the mixed-root routing contract and prove its fault controls before adding the live binding.
/// </remarks>
public sealed partial class Phase02ExplorerRoutingContractTests
{
    private const string SharedSource = "main.asm";
    private const string FolderSource = "scratch.asm";
    private const string ProjectSource = "startup.asm";

    /// <summary>Accepts the reference route and detects individual root, branch, and document identity faults.</summary>
    [Theory]
    [InlineData("duplicate-root")]
    [InlineData("wrong-branch")]
    [InlineData("wrong-document")]
    [InlineData("duplicate-document")]
    public async Task Scripted_routing_accepts_the_contract_and_rejects_the_fault(string fault)
    {
        using var inputs = new ExplorerInputs();
        await VerifyMixedRootsAsync(new ScriptedExplorerPort(inputs), inputs);
        await Assert.ThrowsAnyAsync<XunitException>(() => VerifyMixedRootsAsync(new ScriptedExplorerPort(inputs, fault), inputs));
    }

    private static async Task VerifyMixedRootsAsync(IExplorerPort explorer, ExplorerInputs inputs)
    {
        Assert.Empty(await explorer.ReadRootsAsync());
        var folder = await explorer.OpenRootAsync(inputs.Folder, "folder");
        Assert.NotEqual(Guid.Empty, folder.Id);
        Assert.Equal(inputs.Folder, folder.Path);
        Assert.Equal("folder", folder.Kind);
        Assert.Equal(folder, Assert.Single(await explorer.ReadRootsAsync()));

        var project = await explorer.OpenRootAsync(inputs.Project, "project");
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.NotEqual(folder.Id, project.Id);
        Assert.Equal(inputs.Project, project.Path);
        Assert.Equal("project", project.Kind);
        Assert.Equal([folder, project], (await explorer.ReadRootsAsync()).OrderBy(root => root.Kind, StringComparer.Ordinal));

        var folderBranch = await explorer.ReadBranchAsync(folder.Id);
        var projectBranch = await explorer.ReadBranchAsync(project.Id);
        Assert.Equal(folder.Id, folderBranch.RootId);
        Assert.Equal(project.Id, projectBranch.RootId);
        Assert.True(folderBranch.Available);
        Assert.True(projectBranch.Available);
        Assert.Equal([SharedSource, FolderSource], folderBranch.Files.Order(StringComparer.Ordinal));
        Assert.Equal([SharedSource, ProjectSource], projectBranch.Files.Order(StringComparer.Ordinal));

        var first = await explorer.OpenDocumentAsync(folder.Id, SharedSource);
        var second = await explorer.OpenDocumentAsync(project.Id, SharedSource);
        Assert.NotEqual(Guid.Empty, first.ViewKey);
        Assert.NotEqual(Guid.Empty, second.ViewKey);
        Assert.NotEqual(first.ViewKey, second.ViewKey);
        Assert.NotEqual(first.DocumentId, second.DocumentId);
        Assert.Equal(folder.Id, first.RootId);
        Assert.Equal(project.Id, second.RootId);
        Assert.Equal(SharedSource, first.RelativePath);
        Assert.Equal(SharedSource, second.RelativePath);
        Assert.Equal(first, await explorer.OpenDocumentAsync(folder.Id, SharedSource));
        Assert.Equal(second, await explorer.OpenDocumentAsync(project.Id, SharedSource));
    }

    private interface IExplorerPort
    {
        Task<IReadOnlyList<RootInfo>> ReadRootsAsync();
        Task<RootInfo> OpenRootAsync(string path, string kind);
        Task<BranchInfo> ReadBranchAsync(Guid rootId);
        Task<DocumentInfo> OpenDocumentAsync(Guid rootId, string relativePath);
    }

    private sealed record RootInfo(Guid Id, string Kind, string Path);
    private sealed record BranchInfo(Guid RootId, bool Available, IReadOnlyList<string> Files);
    private sealed record DocumentInfo(Guid ViewKey, Guid DocumentId, Guid RootId, string RelativePath);

    private sealed class ExplorerInputs : IDisposable
    {
        private readonly DirectoryInfo _fixture = Directory.CreateTempSubdirectory("retro-ide-explorer-contract-");

        public ExplorerInputs()
        {
            Folder = _fixture.CreateSubdirectory("folder").FullName;
            Project = _fixture.CreateSubdirectory("project").FullName;
            File.WriteAllText(Path.Combine(Folder, SharedSource), "folder source");
            File.WriteAllText(Path.Combine(Folder, FolderSource), "scratch source");
            File.WriteAllText(Path.Combine(Project, SharedSource), "project source");
            File.WriteAllText(Path.Combine(Project, ProjectSource), "startup source");
        }

        public string Folder { get; }
        public string Project { get; }

        public void Dispose() => _fixture.Delete(recursive: true);
    }

    private sealed class ScriptedExplorerPort(ExplorerInputs inputs, string? fault = null) : IExplorerPort
    {
        private readonly Guid _folderId = Guid.NewGuid();
        private readonly Guid _projectId = Guid.NewGuid();
        private readonly List<RootInfo> _roots = [];
        private readonly Dictionary<Guid, DocumentInfo> _documents = [];

        public Task<IReadOnlyList<RootInfo>> ReadRootsAsync() => Task.FromResult<IReadOnlyList<RootInfo>>(_roots.ToArray());

        public Task<RootInfo> OpenRootAsync(string path, string kind)
        {
            var folder = string.Equals(path, inputs.Folder, StringComparison.Ordinal);
            Assert.Equal(folder ? "folder" : "project", kind);
            if (!folder)
                Assert.Equal(inputs.Project, path);

            var root = new RootInfo(folder || IsFault("duplicate-root") ? _folderId : _projectId, kind, path);
            _roots.Add(root);
            return Task.FromResult(root);
        }

        public Task<BranchInfo> ReadBranchAsync(Guid rootId)
        {
            if (rootId == _folderId || IsFault("wrong-branch"))
                return Task.FromResult(new BranchInfo(_folderId, true, [FolderSource, SharedSource]));

            Assert.Equal(_projectId, rootId);
            return Task.FromResult(new BranchInfo(_projectId, true, [ProjectSource, SharedSource]));
        }

        public Task<DocumentInfo> OpenDocumentAsync(Guid rootId, string relativePath)
        {
            Assert.Equal(SharedSource, relativePath);
            Assert.Contains(_roots, root => root.Id == rootId);
            var destination = IsFault("wrong-document") ? _folderId : rootId;
            if (!IsFault("duplicate-document") && _documents.TryGetValue(destination, out var document))
                return Task.FromResult(document);

            var opened = new DocumentInfo(Guid.NewGuid(), Guid.NewGuid(), destination, relativePath);
            _documents[destination] = opened;
            return Task.FromResult(opened);
        }

        private bool IsFault(string candidate) => string.Equals(fault, candidate, StringComparison.Ordinal);
    }
}
