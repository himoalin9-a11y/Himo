namespace Himo.Services;

public static class UiMotion
{
    public static async Task FadeInAsync(VisualElement? root, uint duration = 180)
    {
        if (root is null) return;
        root.Opacity = 0;
        await root.FadeToAsync(1, duration, Easing.CubicOut);
    }

    public static async Task PressAsync(VisualElement? element)
    {
        if (element is null) return;
        await element.ScaleToAsync(0.97, 60, Easing.CubicOut);
        await element.ScaleToAsync(1, 70, Easing.CubicOut);
    }
}
