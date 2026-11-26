using System.Globalization;

namespace RetroIde.Application.Git;

/// <summary>Describes the status of one captured worktree, including branch and upstream context.</summary>
/// <remarks>2026-11-26 · TR-RETRO-GIT-013#ac-git-machine-state. Parse Git porcelain version 2 without splitting path text at whitespace.</remarks>
internal sealed record GitStatus(GitWorktree Repository, string Branch, string? Head, string? Upstream, int? Ahead, int? Behind, GitStatusEntry[] Entries)
{
    /// <summary>Gets the worktree-relative Git index paths within the captured workspace scope.</summary>
    public string[] TrackedPaths { get; init; } = [];

    /// <summary>Gets the operation paused in the selected worktree, when it has an active native operation.</summary>
    public string? OperationKind { get; init; }

    /// <summary>Parses one NUL-terminated native status response.</summary>
    /// <param name="repository">The worktree captured for the invocation.</param>
    /// <param name="status">The complete Git porcelain v2 status, fetched with branch headers.</param>
    /// <returns>The decoded status entries and branch information.</returns>
    /// <exception cref="InvalidDataException">A required header, path, or record is malformed.</exception>
    internal static GitStatus Parse(GitWorktree repository, string status)
    {
        if (!status.EndsWith('\0'))
            throw new InvalidDataException("Git status is missing its last record terminator.");

        var records = status.Split('\0');
        var entries = new List<GitStatusEntry>();
        string? branch = null;
        string? head = null;
        string? upstream = null;
        int? ahead = null;
        int? behind = null;
        for (var index = 0; index < records.Length - 1; index++)
        {
            var record = records[index];
            if (!record.StartsWith('#'))
            {
                entries.Add(ParseEntry(records, ref index));
                continue;
            }
            const string BranchHeader = "# branch.head ";
            const string HeadHeader = "# branch.oid ";
            const string UpstreamHeader = "# branch.upstream ";
            const string CountsHeader = "# branch.ab ";
            if (record.StartsWith(BranchHeader, StringComparison.Ordinal))
                branch = record[BranchHeader.Length..];
            else if (record.StartsWith(HeadHeader, StringComparison.Ordinal))
            {
                var oid = record[HeadHeader.Length..];
                head = string.Equals(oid, "(initial)", StringComparison.Ordinal) ? null : oid;
            }
            else if (record.StartsWith(UpstreamHeader, StringComparison.Ordinal))
                upstream = record[UpstreamHeader.Length..];
            else if (record.StartsWith(CountsHeader, StringComparison.Ordinal))
            {
                var parts = record[CountsHeader.Length..].Split(' ');
                if (parts.Length != 2 || !parts[0].StartsWith('+') || !parts[1].StartsWith('-') ||
                    !int.TryParse(parts[0].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var a) ||
                    !int.TryParse(parts[1].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                    throw new InvalidDataException("Git status has invalid upstream counts.");
                ahead = a;
                behind = b;
            }
        }
        if (string.IsNullOrEmpty(branch))
            throw new InvalidDataException("Git status did not identify the checked-out branch or detached HEAD.");
        return new GitStatus(repository, branch, head, upstream, ahead, behind, [.. entries]);
    }

    /// <summary>Decodes one path entry, consuming the following original path for a rename or copy.</summary>
    /// <param name="records">The NUL-separated response records.</param>
    /// <param name="index">The current record position, advanced for a rename or copy.</param>
    /// <returns>The literal path and its recorded states.</returns>
    private static GitStatusEntry ParseEntry(string[] records, ref int index)
    {
        var record = records[index];
        if (record.Length < 3 || record[1] != ' ')
            throw new InvalidDataException("Git status has an invalid path record.");
        if (record[0] is '?' or '!')
        {
            var indicator = record[..1];
            return new GitStatusEntry(record[2..], null, indicator, indicator, IsUnmerged: false, Submodule: null);
        }
        var fieldCount = record[0] switch { '1' => 9, '2' => 10, 'u' => 11, _ => 0 };
        var fields = record.Split(' ', fieldCount);
        if (fieldCount == 0 || fields.Length != fieldCount || fields[1].Length != 2 || fields[^1].Length == 0)
            throw new InvalidDataException("Git status has an unsupported tracked-path record.");

        string? original = null;
        if (record[0] == '2')
        {
            if (++index >= records.Length - 1 || records[index].Length == 0)
                throw new InvalidDataException("Git status has a rename without its original path.");
            original = records[index];
        }
        return new GitStatusEntry(fields[^1], original, fields[1][..1], fields[1][1..], record[0] == 'u', fields[2]);
    }
}
