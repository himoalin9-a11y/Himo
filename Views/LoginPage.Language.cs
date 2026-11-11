using System;
using System.Linq;
using Himo.Services;

namespace Himo.Views;

public partial class LoginPage
{
    private static readonly (string Code, string ResourceKey)[] LoginLanguageOptions =
    {
        ("en", "LanguageEnglish"),
        ("ar", "LanguageArabic"),
        ("fr", "LanguageFrench"),
        ("tr", "LanguageTurkish"),
        ("ur", "LanguageUrdu"),
        ("es", "LanguageSpanish"),
        ("id", "LanguageIndonesian"),
        ("de", "LanguageGerman"),
        ("pt", "LanguagePortuguese"),
        ("zh-Hans", "LanguageChineseSimplified"),
        ("hi", "LanguageHindi"),
        ("ja", "LanguageJapanese")
    };

    public void InitializeLoginLanguageSelector() => RefreshLoginLanguageButton();

    private void RefreshLoginLanguageButton()
    {
        if (LoginLanguageButton is null)
            return;

        var selected = LoginLanguageOptions.FirstOrDefault(
            option => string.Equals(option.Code, AppLocalization.CurrentLanguageCode, StringComparison.Ordinal));

        var label = string.IsNullOrWhiteSpace(selected.Code)
            ? AppLocalization.T("LanguageEnglish")
            : AppLocalization.T(selected.ResourceKey);

        LoginLanguageButton.Text = "🌐 " + label;
        LoginLanguageButton.FlowDirection = Microsoft.Maui.FlowDirection.LeftToRight;
    }

    private async void LoginLanguageButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            var options = LoginLanguageOptions
                .Select(option => new
                {
                    option.Code,
                    option.ResourceKey,
                    Label = AppLocalization.T(option.ResourceKey)
                })
                .ToArray();

            var selectedLabel = await DisplayActionSheetAsync(
                AppLocalization.T("LanguageTitle"),
                AppLocalization.T("Cancel"),
                null,
                options.Select(option => option.Label).ToArray());

            if (string.IsNullOrWhiteSpace(selectedLabel))
                return;

            var selected = options.FirstOrDefault(option =>
                string.Equals(option.Label, selectedLabel, StringComparison.Ordinal));

            if (selected is null)
                return;

            AppLocalization.SetLanguage(selected.Code);
            AppLocalization.AttachToPage(this);
            RefreshLoginLanguageButton();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Himo Login Language] " + ex);
            await DisplayAlertAsync(
                AppLocalization.T("LanguageTitle"),
                Himo.Services.AppLocalization.TranslateSourceText(ex.Message),
                AppLocalization.T("Cancel"));
        }
    }
}
