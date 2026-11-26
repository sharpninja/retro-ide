using System.ComponentModel;
using System.Globalization;
using System.Text;
using RetroIde.Application.Workspaces;

namespace RetroIde.Application.Git;

internal static class GitBlameOperations
{
    internal sealed record Input(string WorktreePath, string Path, string? CommitId = null, int? FirstLine = null, int? LastLine = null);
    internal sealed record CloseInput(Guid ViewKey);
    internal sealed record Line(int LineNumber, int OriginLine, string? OriginPath, string? CommitId, bool Uncommitted, string Text,
        string? Author, string? AuthorEmail, long? AuthorUnixTime, string? AuthorOffset, string? Summary);
    internal sealed record Blame(GitWorktree Repository, Input Source, Line[] Lines);
    internal sealed record Tab(Guid ViewKey, Guid WorkspaceId, Blame Blame);

    internal static async Task<FeatureOutcome> ReadAsync(Input input, IWorkbenchRegistration view, bool openTab, CancellationToken token)
    {
        if (view is not IGitRegistration { SelectedGit: { } selection })
            return new("git_selection_required", "Select a Git worktree first.");
        var repo = selection.Repository;
        if (!string.Equals(repo.WorktreePath, input.WorktreePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return new("git_worktree_mismatch", "The requested worktree differs from the current selection.");
        if (input is { FirstLine: <= 0 } or { LastLine: <= 0 } || input.LastLine < input.FirstLine ||
            (input.CommitId is not null && !GitHistoryOperations.IsObjectId(input.CommitId)))
            return new("git_blame_input_invalid", "Use positive line numbers and a full commit ID for historical attribution.");

        try
        {
            if (!GitFileOperations.IsScopedFile(selection, input.Path))
                return new("git_scope_invalid", "Choose a literal file path inside the selected worktree scope.");
            var found = await NativeGit.DiscoverAsync(selection.ScopeRoot, "", token).ConfigureAwait(false);
            if (found is null || !NativeGit.IsSameWorktree(found.Repository, repo))
                return new("git_repository_unavailable", "The selected Git worktree is unavailable.");

            var relative = input.Path.Replace('/', Path.DirectorySeparatorChar);
            if (input.CommitId is null && WorkspaceExplorer.TraversesLink(repo.WorktreePath, relative))
                return new("git_scope_invalid", "Choose a working file whose path stays inside the selected worktree.");
            var args = NativeGit.WorktreeArguments(repo);
            args.AddRange(["-c", "core.quotePath=false", "blame", "--line-porcelain", "--encoding=UTF-8", "--root"]);
            if (input is { FirstLine: not null } or { LastLine: not null })
                args.AddRange(["-L", FormattableString.Invariant($"{input.FirstLine ?? 1},{input.LastLine}")]);
            if (input.CommitId is { } commit)
                args.Add(commit);
            args.AddRange(["--", input.Path]);
            var result = await NativeGit.ReadBytesAsync(repo.WorktreePath, args, token).ConfigureAwait(false);
            var source = input with { WorktreePath = repo.WorktreePath, CommitId = input.CommitId?.ToLowerInvariant() };
            return result is null ? Unavailable() : Prepare(new Blame(repo, source, Parse(Encoding.UTF8.GetString(result), selection)), selection, openTab);
        }
        catch (Win32Exception) { return new("git_unavailable", "Git could not be started."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Unavailable();
        }
    }

    private static FeatureOutcome Prepare(Blame blame, GitSelection selection, bool openTab) => new("ok", "Git blame read.")
    {
        Commit = current =>
        {
            if (current is not IGitRegistration git || !ReferenceEquals(git.SelectedGit, selection))
                return GitOperationRecovery.ChangedSelection();
            if (!openTab)
                return FeatureOutcome.Success("Git blame read.", blame);
            return git.ShowBlame(blame) is { } tab
                ? FeatureOutcome.Success("Git blame tab opened.", tab)
                : new("view_closed", "The workbench cannot open a Git blame tab.");
        },
    };

    private static Line[] Parse(string output, GitSelection selection)
    {
        var lines = new List<Line>();
        Line? current = null;
        foreach (var text in output.Split('\n', StringSplitOptions.None))
        {
            if (current is null)
            {
                if (text.Length == 0)
                    continue;
                var header = text.Split(' ', 4);
                if (header.Length < 3 || !GitHistoryOperations.IsObjectId(header[0]) ||
                    !int.TryParse(header[1], NumberStyles.None, CultureInfo.InvariantCulture, out var origin) ||
                    !int.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out var line))
                    throw new InvalidDataException("Git blame returned an invalid porcelain header.");
                var uncommitted = header[0].All(character => character == '0');
                current = new Line(line, origin, null, uncommitted ? null : header[0], uncommitted, "", null, null, null, null, null);
            }
            else if (text.StartsWith('\t'))
            {
                lines.Add(current with { Text = text[1..] });
                current = null;
            }
            else
                current = ReadMetadata(current, text, selection);
        }
        return current is null ? [.. lines] : throw new InvalidDataException("Git blame returned an incomplete line.");
    }

    private static Line ReadMetadata(Line current, string text, GitSelection selection)
    {
        var separator = text.IndexOf(' ', StringComparison.Ordinal);
        if (separator < 0)
            return current;
        var value = text[(separator + 1)..];
        return text[..separator] switch
        {
            "author" => current with { Author = value },
            "author-mail" => current with { AuthorEmail = value.Trim('<', '>') },
            "author-time" when long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds) =>
                current with { AuthorUnixTime = seconds },
            "author-tz" => current with { AuthorOffset = value },
            "summary" => current with { Summary = value },
            "filename" when DecodePath(value) is { } path && GitFileOperations.IsScopedFile(selection, path) =>
                current with { OriginPath = path },
            _ => current,
        };
    }

    private static string? DecodePath(string value)
    {
        if (!value.StartsWith('"'))
            return value;
        if (value.Length < 2 || !value.EndsWith('"'))
            return null;
        using var bytes = new MemoryStream();
        var content = value[1..^1];
        var index = 0;
        while (index < content.Length)
        {
            var escaped = content.IndexOf('\\', index);
            if (escaped < 0)
                escaped = content.Length;
            if (escaped > index)
                bytes.Write(Encoding.UTF8.GetBytes(content[index..escaped]));
            index = escaped + 1;
            if (escaped == content.Length)
                break;
            if (index >= content.Length)
                return null;
            var character = content[index++];
            if (character is >= '0' and <= '3')
            {
                if (index + 1 >= content.Length || !IsOctal(content[index]) || !IsOctal(content[index + 1]))
                    return null;
                bytes.WriteByte((byte)((character - '0') * 64 + (content[index] - '0') * 8 + content[index + 1] - '0'));
                index += 2;
            }
            else
            {
                var decoded = "abfnrtv\\\"".IndexOf(character, StringComparison.Ordinal);
                if (decoded < 0)
                    return null;
                bytes.WriteByte((byte)"\a\b\f\n\r\t\v\\\""[decoded]);
            }
        }
        try { return new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
        catch (DecoderFallbackException) { return null; }

        static bool IsOctal(char c) => c is >= '0' and <= '7';
    }

    private static FeatureOutcome Unavailable() => new("git_blame_unavailable", "Git could not attribute the selected file and line range.");
}
