using RetroIde.Application;

namespace RetroIde.Workbench;

internal abstract class LanguageToolViewModel(WorkbenchViewModel workbench) : DockedToolViewModel(workbench)
{
    protected void ReportInputError(string slug) =>
        Receive(OperationResult.Failure(Guid.NewGuid(), "invalid_language_configuration", Localization.Text(slug)));
}
