using Avalonia.Threading;
using RetroIde.Application;
using RetroIde.Application.LanguageServers;
using RetroIde.Application.Workspaces;

namespace RetroIde.Workbench;

internal sealed partial class DocumentController
{
    internal FeatureOutcome ApplyLanguageEdits(LanguageBufferEdit[] edits)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (edits.Any(edit => !CanPrepareLanguageEdit(edit.File)))
            return Changed();

        var opened = new List<DocumentViewModel>();
        var targets = new List<(DocumentViewModel View, LanguageBufferEdit Edit)>();
        try
        {
            foreach (var edit in edits)
            {
                if (edit.File.Source is { } source)
                {
                    if (OpenOrActivate(edit.File.Address.Entry, source, null) is null)
                    {
                        ClosePreparedLanguageEditors(opened);
                        return Changed();
                    }
                    opened.Add(_open[edit.File.Address.Entry].Source!);
                }
                var view = _open[edit.File.Address.Entry].Source!;
                targets.Add((view, edit));
            }
            if (targets.Any(target => !string.Equals(target.View.Buffer.Text, target.Edit.File.Text, StringComparison.Ordinal)))
            {
                ClosePreparedLanguageEditors(opened);
                return Changed();
            }

            foreach (var (view, edit) in targets)
            {
                using (view.Buffer.RunUpdate())
                    for (var index = edit.Edits.Length - 1; index >= 0; index--)
                    {
                        var change = edit.Edits[index];
                        view.Buffer.Replace(change.Offset, change.Length, change.NewText);
                    }
            }
            return FeatureOutcome.Success("Workspace edit applied to the source buffers.",
                targets.Select(target => target.View.Capture()).ToArray());
        }
        catch (Exception error) when (RuntimeFailures.CanRecover(error))
        {
            _dispatch.ReportLanguageFailure(error, "Apply language server workspace edit");
            RestoreLanguageBuffers(targets);
            ClosePreparedLanguageEditors(opened);
            return new("language_edit_failed", "The workspace edit could not be applied.");
        }
    }

    private bool CanPrepareLanguageEdit(LanguageTargetFile file)
    {
        var address = file.Address.Entry;
        if (IsSwitchingPath(Path.GetFullPath(address.RelativePath, file.Address.RootPath)))
            return false;
        return file.OpenBuffer is { } open ? TryResolve(open.Context, out _) : !_open.ContainsKey(address);
    }

    private void RestoreLanguageBuffers(IEnumerable<(DocumentViewModel View, LanguageBufferEdit Edit)> targets)
    {
        foreach (var (view, edit) in targets.Reverse())
        {
            if (string.Equals(view.Buffer.Text, edit.File.Text, StringComparison.Ordinal))
                continue;
            try
            {
                var history = view.Buffer.UndoStack;
                if (history.CanUndo)
                    history.Undo();
                if (!string.Equals(view.Buffer.Text, edit.File.Text, StringComparison.Ordinal))
                    view.Buffer.Replace(0, view.Buffer.TextLength, edit.File.Text);
            }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                _dispatch.ReportLanguageFailure(error, "Restore editor buffer after a failed language workspace edit");
            }
        }
    }

    private void ClosePreparedLanguageEditors(IEnumerable<DocumentViewModel> opened)
    {
        foreach (var editor in opened.Reverse())
        {
            try { Close(new(editor.Capture(), DiscardChanges: true)); }
            catch (Exception error) when (RuntimeFailures.CanRecover(error))
            {
                _dispatch.ReportLanguageFailure(error, "Close editor opened for a failed workspace edit");
            }
        }
    }
}
