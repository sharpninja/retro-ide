namespace RetroIde.Application.Git;

internal static class GitTagOperations
{
    internal const string Prefix = "refs/tags/";
    private const string Fields = "%(refname)%00%(objectname)%00%(objecttype)%00%(*objectname)%00%(*objecttype)%00" +
        "%(taggername)%00%(taggeremail)%00%(taggerdate:iso-strict)%00%(contents:subject)%00%(contents:body)%00%(symref)%00";

    internal sealed record ReadInput(string WorktreePath, string TagName, string? ExpectedObjectId = null);
    internal sealed record CreateInput(string WorktreePath, string TagName, string TargetCommit, string? Message = null);
    internal sealed record DeleteInput(string WorktreePath, string TagName, string ExpectedObjectId);
    internal sealed record Tag(GitWorktree Repository, string TagName, string ObjectId, string ObjectType,
        string TargetObjectId, string TargetObjectType, string? TaggerName = null, string? TaggerEmail = null,
        string? TaggerDate = null, string? Subject = null, string? Body = null, string? SymbolicTarget = null);

    internal static Task<FeatureOutcome> InspectAsync(ReadInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithTagAsync(input.WorktreePath, input.TagName, view, async (selection, name) =>
        {
            if (input.ExpectedObjectId is not null && !GitHistoryOperations.IsObjectId(input.ExpectedObjectId))
                return InvalidObject();
            var tag = await ReadAsync(selection.Repository, name, token).ConfigureAwait(false);
            if (tag is null)
                return Unavailable();
            if (input.ExpectedObjectId is not null && !Matches(tag, input.ExpectedObjectId))
                return Changed();
            return new FeatureOutcome("ok", "Git tag read.")
            {
                Commit = current => GitOperationRecovery.SameSelection(current, selection)
                    ? FeatureOutcome.Success("Git tag read.", tag) : GitOperationRecovery.ChangedSelection(),
            };
        }, token);

    internal static Task<FeatureOutcome> CreateAsync(CreateInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithTagAsync(input.WorktreePath, input.TagName, view, async (selection, name) =>
        {
            if (!GitHistoryOperations.IsObjectId(input.TargetCommit))
                return InvalidObject();
            if (input.Message is { } message && (string.IsNullOrWhiteSpace(message) || message.Contains('\0')))
                return new("git_tag_message_invalid", "Supply a nonempty annotation or omit the message.");
            var repo = selection.Repository;
            if (await ReadAsync(repo, name, token).ConfigureAwait(false) is not null)
                return Exists();
            var type = await ReadNativeAsync(repo, token, "cat-file", "-t", input.TargetCommit).ConfigureAwait(false);
            if (!string.Equals(type?.Trim(), "commit", StringComparison.Ordinal))
                return new("git_tag_target_unavailable", "The selected object is not an available commit.");
            return Authorize(selection, cancel => ExecuteCreateAsync(repo, input, cancel));
        }, token);

    internal static Task<FeatureOutcome> DeleteAsync(DeleteInput input, IWorkbenchRegistration view, CancellationToken token) =>
        WithTagAsync(input.WorktreePath, input.TagName, view, async (selection, name) =>
        {
            if (!GitHistoryOperations.IsObjectId(input.ExpectedObjectId))
                return InvalidObject();
            var tag = await ReadAsync(selection.Repository, name, token).ConfigureAwait(false);
            if (DeleteProblem(tag, input.ExpectedObjectId) is { } failure)
                return failure;
            return Authorize(selection, cancel => ExecuteDeleteAsync(tag!, name, cancel));
        }, token);

    private static async Task<FeatureOutcome> ExecuteCreateAsync(GitWorktree repo, CreateInput input, CancellationToken token)
    {
        var args = NativeGit.WorktreeArguments(repo);
        args.Add("tag");
        if (input.Message is { } message)
            args.AddRange(["--annotate", "--message", message]);
        args.AddRange(["--", input.TagName, input.TargetCommit]);
        token.ThrowIfCancellationRequested();
        var result = await NativeGit.RunCaptureAsync(repo.WorktreePath, args, CancellationToken.None).ConfigureAwait(false);
        var tag = await ReadAsync(repo, Prefix + input.TagName, CancellationToken.None).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return tag is not null ? Exists() : new("git_tag_create_failed", "Native Git could not create the tag.");
        return tag is not null && string.Equals(tag.TargetObjectId, input.TargetCommit, StringComparison.OrdinalIgnoreCase)
            ? FeatureOutcome.Success("Git tag created.", tag) : Changed();
    }

    private static async Task<FeatureOutcome> ExecuteDeleteAsync(Tag tag, string fullName, CancellationToken token)
    {
        var repo = tag.Repository;
        var args = NativeGit.WorktreeArguments(repo);
        args.AddRange(["update-ref", "--no-deref", "-d", fullName, tag.ObjectId]);
        token.ThrowIfCancellationRequested();
        var deleted = await NativeGit.RunCaptureAsync(repo.WorktreePath, args, CancellationToken.None).ConfigureAwait(false);
        if (deleted.ExitCode == 0)
            return FeatureOutcome.Success("Git tag deleted.", tag);
        return DeleteProblem(await ReadAsync(repo, fullName, CancellationToken.None).ConfigureAwait(false), tag.ObjectId) ??
            new("git_tag_delete_failed", "Native Git could not delete the tag.");
    }

    private static async Task<Tag?> ReadAsync(GitWorktree repo, string fullName, CancellationToken token)
    {
        var output = await ReadNativeAsync(repo, token, "for-each-ref", "--count=1", $"--format={Fields}", fullName).ConfigureAwait(false)
            ?? throw new InvalidDataException("Native Git could not read the tag.");
        if (output.Length == 0)
            return null;
        var fields = output.Split('\0');
        if (fields.Length != 12 || !GitHistoryOperations.IsObjectId(fields[1]))
            throw new InvalidDataException("Native Git returned incomplete tag metadata.");
        if (!string.Equals(fields[0], fullName, StringComparison.Ordinal))
            return null;
        var annotated = string.Equals(fields[2], "tag", StringComparison.Ordinal);
        return new Tag(repo, fullName[Prefix.Length..], fields[1], fields[2], Optional(fields[3]) ?? fields[1],
            Optional(fields[4]) ?? fields[2], Optional(fields[5]), Optional(fields[6].Trim('<', '>')), Optional(fields[7]),
            annotated ? Optional(fields[8]) : null, annotated ? Optional(fields[9]) : null, Optional(fields[10]));
    }

    private static Task<FeatureOutcome> WithTagAsync(string worktree, string tagName, IWorkbenchRegistration view,
        Func<GitSelection, string, Task<FeatureOutcome>> action, CancellationToken token) =>
        GitOperationRecovery.WithSelectionAsync(worktree, view, async (_, selection) =>
        {
            if (string.IsNullOrWhiteSpace(tagName) || tagName.StartsWith('-') || tagName.Contains('\0'))
                return InvalidName();
            var repo = selection.Repository;
            if (!await GitMutationLease.IsCurrentAsync(repo, token).ConfigureAwait(false))
                return GitMutationLease.WorktreeUnavailable();
            var name = Prefix + tagName;
            if (await ReadNativeAsync(repo, token, "check-ref-format", name).ConfigureAwait(false) is null)
                return InvalidName();
            return await action(selection, name).ConfigureAwait(false);
        }, token);

    private static FeatureOutcome Authorize(GitSelection selection, Func<CancellationToken, Task<FeatureOutcome>> execute) =>
        new("ok", "Git tag operation prepared.")
        {
            Commit = current => GitOperationRecovery.SameSelection(current, selection)
                ? new FeatureOutcome("ok", "Git tag operation authorized.")
                {
                    ExecuteAuthorizedAsync = token => GitOperationRecovery.TryAsync(
                        () => GitMutationLease.RunAsync(selection.Repository, token, () => execute(token))),
                }
                : GitOperationRecovery.ChangedSelection(),
        };

    private static Task<string?> ReadNativeAsync(GitWorktree repo, CancellationToken token, params string[] args) =>
        NativeGit.RunAsync(repo.WorktreePath, [.. NativeGit.WorktreeArguments(repo), .. args], token);

    private static bool Matches(Tag tag, string expected) => string.Equals(tag.ObjectId, expected, StringComparison.OrdinalIgnoreCase);
    private static string? Optional(string value) => value.Length == 0 ? null : value;
    private static FeatureOutcome InvalidObject() => new("git_tag_object_invalid", "Supply a full Git object ID.");
    private static FeatureOutcome InvalidName() => new("git_tag_name_invalid", "Supply a valid Git tag name.");
    private static FeatureOutcome Exists() => new("git_tag_exists", "A Git tag already has that name.");
    private static FeatureOutcome Changed() => new("git_tag_changed", "The Git tag changed. Review the current tag before retrying.");
    private static FeatureOutcome Unavailable() => new("git_tag_unavailable", "The selected Git tag is unavailable.");
    private static FeatureOutcome? DeleteProblem(Tag? tag, string expected) =>
        tag is null ? Unavailable() : !Matches(tag, expected) ? Changed() :
        tag.SymbolicTarget is not null ? new("git_tag_symbolic", "Choose a direct Git tag before deleting it.") : null;
}
