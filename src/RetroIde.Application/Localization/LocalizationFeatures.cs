namespace RetroIde.Application.Localization;

internal sealed class LocalizationFeatures
{
    internal sealed record ReadLanguageInput;
    internal sealed record SetLanguageInput(string Locale);
    internal sealed record LanguageState(string Locale, bool RightToLeft, IReadOnlyList<string> AvailableLocales);

    public Feature<ReadLanguageInput> GetLanguage { get; } = new(
        "workbench.language.read", "workbench_get_display_language", "Read the display language and installed locale packs.", false,
        static (_, view) => view is ILocalizationRegistration localization
            ? FeatureOutcome.Success("Display language read.", localization.Language)
            : new("language_unavailable", "Display language is unavailable."), typeof(LanguageState));

    public Feature<SetLanguageInput> SetLanguage { get; } = new(
        "workbench.language.set", "workbench_set_display_language", "Change and persist the display language of the workbench.", true,
        static (input, view) => view is ILocalizationRegistration localization
            ? localization.SetLanguage(input.Locale)
            : new("language_unavailable", "Display language is unavailable."), typeof(LanguageState));
}
