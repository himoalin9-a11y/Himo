using System;
using Microsoft.Maui.Controls;

namespace Himo.Services;

/// <summary>
/// Base page for C#-constructed pages. It applies resource-backed text when the
/// page appears and refreshes visible modal/navigation pages when the language changes.
/// </summary>
public class LocalizedContentPage : ContentPage
{
    public LocalizedContentPage()
    {
        Appearing += HandleAppearing;
        Disappearing += HandleDisappearing;
    }

    private void HandleAppearing(object? sender, EventArgs e)
    {
        AppLocalization.LanguageChanged -= HandleLanguageChanged;
        AppLocalization.LanguageChanged += HandleLanguageChanged;
        _ = AppLocalization.ApplyToPageInBatchesAsync(this, batchSize: 64, skipCollectionViewContents: true);
    }

    private void HandleDisappearing(object? sender, EventArgs e)
        => AppLocalization.LanguageChanged -= HandleLanguageChanged;

    private void HandleLanguageChanged(object? sender, EventArgs e)
    {
        if (IsVisible)
            _ = AppLocalization.ApplyToPageInBatchesAsync(this, batchSize: 64, skipCollectionViewContents: true);
    }
}
