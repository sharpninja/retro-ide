using System.Text;
using System.Text.Json.Serialization;
using RetroIde.Application.Git;

namespace RetroIde.Application.Workspaces;

internal sealed class WorkspaceExplorer
{
    [JsonConverter(typeof(JsonStringEnumConverter<RootKind>))]
    internal enum RootKind { Folder, Project }
    internal sealed record Root(Guid Id, RootKind Kind, string Path);
    internal readonly record struct Address(Guid RootId, string RelativePath);
    internal sealed record Entry(Address Address, string Name, bool IsDirectory);
    internal sealed record Branch(Guid WorkspaceId, Address Parent, bool IsAvailable, IReadOnlyList<Entry> Entries);
    internal sealed record SourceAddress(Address Entry, string RootPath);
    internal sealed record SourceFile(SourceAddress Address, string Text, Encoding Encoding);

    internal sealed record State(int Version, Guid WorkspaceId, Root[] Roots, Address[] Expanded, Address? Selection = null,
        GitWorkspaceLocation? GitSelection = null);

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Root> _roots = [];
    private readonly HashSet<Address> _expanded = new(AddressEquality);
    private Address? _selection;
    private GitWorkspaceLocation? _gitSelection;

    public WorkspaceExplorer(Guid workspaceId, IEnumerable<Root>? savedRoots = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(workspaceId, Guid.Empty);
        WorkspaceId = workspaceId;
        foreach (var root in savedRoots ?? [])
        {
            ArgumentNullException.ThrowIfNull(root);
            var directory = NormalizeRoot(root.Path);
            if (root.Id == Guid.Empty || !Enum.IsDefined(root.Kind) ||
                _roots.Values.Any(existing => PathComparer.Equals(existing.Path, directory)) ||
                !_roots.TryAdd(root.Id, root with { Path = directory }))
                throw new ArgumentException("Saved roots must have unique nonempty identities and directories.", nameof(savedRoots));
        }
    }

    public Guid WorkspaceId { get; }
    internal static IEqualityComparer<Address> AddressEquality { get; } = new AddressComparer();

    public IReadOnlyList<Root> Roots
    {
        get { lock (_gate) return _roots.Values.ToArray(); }
    }

    public Root AddRoot(string directory, RootKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var path = NormalizeRoot(directory);
        lock (_gate)
        {
            var existing = _roots.Values.FirstOrDefault(root => PathComparer.Equals(root.Path, path));
            var root = existing is null ? new Root(Guid.NewGuid(), kind, path) : existing with { Kind = kind };
            _roots[root.Id] = root;
            return root;
        }
    }

    internal bool RemoveRootsUnder(string directory)
    {
        lock (_gate)
        {
            var removed = _roots.Values.Where(root => NativeGit.ContainsPath(directory, root.Path))
                .Select(root => root.Id).ToHashSet();
            if (removed.Count == 0)
                return false;

            foreach (var rootId in removed)
                _roots.Remove(rootId);
            _expanded.RemoveWhere(address => removed.Contains(address.RootId));
            if (_selection is { } selected && removed.Contains(selected.RootId))
                _selection = null;
            if (_gitSelection is { } git && removed.Contains(git.Address.RootId))
                _gitSelection = null;
            return true;
        }
    }

    public bool SetExpanded(Address address, bool isExpanded)
    {
        lock (_gate)
        {
            var normalized = NormalizeAddress(address);
            return isExpanded ? _expanded.Add(normalized) : _expanded.Remove(normalized);
        }
    }

    public bool Select(Address? address)
    {
        lock (_gate)
        {
            var normalized = address is { } value ? NormalizeAddress(value) : (Address?)null;
            if (_selection is { } previous && normalized is { } next && AddressEquality.Equals(previous, next) ||
                _selection is null && normalized is null)
                return false;

            _selection = normalized;
            return true;
        }
    }

    public State Snapshot()
    {
        lock (_gate)
            return new State(1, WorkspaceId, _roots.Values.ToArray(), _expanded.ToArray(), _selection, _gitSelection);
    }

    internal void RememberGitSelection(GitWorkspaceLocation location)
    {
        lock (_gate)
            _gitSelection = location with { Address = NormalizeAddress(location.Address) };
    }

    public static WorkspaceExplorer Restore(State state)
    {
        if (state.Version != 1 || state.Roots is null || state.Expanded is null)
            throw new ArgumentException("Unsupported or incomplete Explorer state.", nameof(state));

        var explorer = new WorkspaceExplorer(state.WorkspaceId, state.Roots);
        foreach (var address in state.Expanded)
            explorer.SetExpanded(address, isExpanded: true);
        explorer.Select(state.Selection);
        if (state.GitSelection is { Repository: not null } git)
        {
            try { explorer.RememberGitSelection(git); }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { }
        }
        return explorer;
    }

    /// <summary>Resolves a file address below its registered workspace root.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-012#ac-test-explorer-mixed-roots. Preserve the source-only constraint while reusing open-root resolution.</remarks>
    public SourceAddress ResolveSource(Address address)
    {
        var resolved = ResolvePath(address);
        if (resolved.Entry.RelativePath.Length == 0)
            throw new ArgumentException("A source file must be below its workspace root.", nameof(address));
        return resolved;
    }

    /// <summary>Resolves an open-root address, including an address that identifies the root itself.</summary>
    /// <remarks>2026-11-26 · TEST-RETRO-013#ac-test-git-multiple-repos. Reuse the Explorer boundary for Git discovery.</remarks>
    /// <param name="address">The root identifier and native relative path.</param>
    /// <returns>The normalized address and its registered root directory.</returns>
    /// <exception cref="ArgumentException">The root is unknown or the relative path escapes it.</exception>
    internal SourceAddress ResolvePath(Address address)
    {
        lock (_gate)
        {
            var normalized = NormalizeAddress(address);
            return new SourceAddress(normalized, _roots[normalized.RootId].Path);
        }
    }

    public void Reveal(Address address)
    {
        lock (_gate)
        {
            var selected = NormalizeAddress(address);
            _selection = selected;
            var parent = Path.GetDirectoryName(selected.RelativePath);
            while (parent is not null)
            {
                _expanded.Add(new Address(selected.RootId, parent));
                parent = Path.GetDirectoryName(parent);
            }
        }
    }

    public static async Task<SourceFile?> ReadSourceAsync(SourceAddress source, CancellationToken token)
    {
        return await Task.Run(async () =>
        {
            var relative = source.Entry.RelativePath;
            try
            {
                if (TraversesLink(source.RootPath, relative))
                    return null;

                var path = Path.Combine(source.RootPath, relative);
                using var reader = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
                var text = await reader.ReadToEndAsync(token).ConfigureAwait(false);
                return text.Contains('\0', StringComparison.Ordinal) ? null : new SourceFile(source, text, reader.CurrentEncoding);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or NotSupportedException)
            {
                return null;
            }
        }, token).ConfigureAwait(false);
    }

    public Task<Branch> ReadChildrenAsync(Address parent, CancellationToken cancellationToken = default)
    {
        Root? root;
        lock (_gate)
            _roots.TryGetValue(parent.RootId, out root);

        // Enumerate only the requested branch and keep filesystem work off the UI dispatcher.
        return Task.Run(() => ReadChildren(root, parent, cancellationToken), cancellationToken);
    }

    private Branch ReadChildren(Root? root, Address parent, CancellationToken cancellationToken)
    {
        if (root is null || parent.RelativePath is null || Path.IsPathRooted(parent.RelativePath))
            return Unavailable();

        try
        {
            var relative = NormalizeRelativePath(root.Path, parent.RelativePath);
            if (relative.Length != 0 && TraversesLink(root.Path, relative))
                return Unavailable();

            var directory = Path.Combine(root.Path, relative);

            var entries = new List<Entry>();
            foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(new Entry(new Address(root.Id, Path.GetRelativePath(root.Path, item.FullName)), item.Name,
                    item.Attributes.HasFlag(FileAttributes.Directory)));
            }

            return new Branch(WorkspaceId, parent, true,
                entries.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, PathComparer).ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Unavailable();
        }

        Branch Unavailable() => new(WorkspaceId, parent, false, []);
    }

    private Address NormalizeAddress(Address address)
    {
        if (!_roots.TryGetValue(address.RootId, out var root))
            throw new ArgumentException("The address must belong to an open root.", nameof(address));

        return address with { RelativePath = NormalizeRelativePath(root.Path, address.RelativePath) };
    }

    private static string NormalizeRelativePath(string root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Path.IsPathRooted(path))
            throw new ArgumentException("The Explorer address must be relative to its root.", nameof(path));

        var fullPath = Path.GetFullPath(path, root);
        var relative = Path.TrimEndingDirectorySeparator(Path.GetRelativePath(root, fullPath));
        if (Path.IsPathRooted(relative) || string.Equals(relative, "..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("The Explorer address escapes its root.", nameof(path));

        return string.Equals(relative, ".", StringComparison.Ordinal) ? string.Empty : relative;
    }

    private sealed class AddressComparer : IEqualityComparer<Address>
    {
        public bool Equals(Address left, Address right) =>
            left.RootId == right.RootId && PathComparer.Equals(left.RelativePath, right.RelativePath);

        public int GetHashCode(Address address) => HashCode.Combine(address.RootId, PathComparer.GetHashCode(address.RelativePath));
    }

    internal static bool TraversesLink(string root, string relative)
    {
        var path = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            path = Path.Combine(path, segment);
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                return true;
        }
        return false;
    }

    private static string NormalizeRoot(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Workspace roots require an absolute directory.", nameof(directory));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }
}
