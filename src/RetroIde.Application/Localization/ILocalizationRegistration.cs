namespace RetroIde.Application.Localization;

internal interface ILocalizationRegistration
{
    LocalizationFeatures.LanguageState Language { get; }
    FeatureOutcome SetLanguage(string locale);
}
