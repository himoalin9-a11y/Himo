using Microsoft.Maui.Controls;

namespace Himo.Services;

/// <summary>
/// Base page for C#-constructed pages. It reapplies resource-backed text each
/// time the page becomes visible, including after a language selection changes.
/// </summary>
public class LocalizedContentPage : ContentPage
{
    public LocalizedContentPage()
    {
        Appearing += (_, _) => AppLocalization.ApplyToPage(this);
    }
}
