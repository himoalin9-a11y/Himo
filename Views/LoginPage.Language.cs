using System;
using System.Diagnostics;
using System.Linq;
using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>
/// Language selector helpers for LoginPage.
/// Intentionally does not override OnAppearing because LoginPage already
/// contains that lifecycle method in its main code-behind file.
/// </summary>
public partial class LoginPage
{
    private static readonly (string Code, string ResourceKey)[] LoginLanguageOptions =
    {
        ("ar", "LanguageArabic"),
        ("en", "LanguageEnglish"),
        ("fr", "LanguageFrench"),
        ("tr", "LanguageTurkish"),
        ("ur", "LanguageUrdu"),
        ("es", "LanguageSpanish"),
        ("id", "LanguageIndonesian")
    };

    private bool _loginLanguageButtonAdded;

    // Call this once from the EXISTING LoginPage.OnAppearing() method,
    // immediately after base.OnAppearing().
    private void EnsureLoginLanguageButton()
    {
        if (_loginLanguageButtonAdded)
            return;

        if (Content is not Grid root)
        {
            Debug.WriteLine("[Himo Login Language] Login root is not a Grid; selector was not attached.");
            return;
        }

        var languageImage = new ImageButton
        {
            Source = "himo_pro_ui_settings_icon_globe.png",
            BackgroundColor = Colors.Transparent,
            Padding = 7,
            WidthRequest = 44,
            HeightRequest = 44,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        SemanticProperties.SetDescription(languageImage, AppLocalization.T("LanguageTitle"));
        languageImage.Clicked += LoginLanguageButtonClicked;

        var languageButton = new Border
        {
            Content = languageImage,
            WidthRequest = 50,
            HeightRequest = 50,
            Padding = 2,
            Margin = new Thickness(18, 18, 18, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Color.FromArgb("#D20B1535"),
            Stroke = new SolidColorBrush(Color.FromArgb("#7657C9")),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = new CornerRadius(16)
            },
            ZIndex = 100
        };

        root.Children.Add(languageButton);
        _loginLanguageButtonAdded = true;
    }

    private async void LoginLanguageButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            var options = LoginLanguageOptions
                .Select(option => (
                    option.Code,
                    option.ResourceKey,
                    Label: AppLocalization.T(option.ResourceKey)))
                .ToArray();

            var selected = await DisplayActionSheetAsync(
                AppLocalization.T("LanguageTitle"),
                AppLocalization.T("Cancel"),
                null,
                options.Select(option => option.Label).ToArray());

            var chosen = options.FirstOrDefault(option =>
                string.Equals(option.Label, selected, StringComparison.Ordinal));

            if (string.IsNullOrWhiteSpace(chosen.Code))
                return;

            AppLocalization.SetLanguage(chosen.Code);
            AppLocalization.AttachToPage(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Himo Login Language] Could not change language: {ex}");
            await DisplayAlertAsync(
                AppLocalization.TranslateSourceText("اللغة"),
                AppLocalization.TranslateSourceText("تعذر تطبيق اللغة المختارة.") + " " + ex.Message,
                AppLocalization.TranslateSourceText("حسنًا"));
        }
    }
}
