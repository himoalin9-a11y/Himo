using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Himo.Services;

/// <summary>
/// Applies a selected interface palette to the running MAUI visual tree and
/// switches the app's built-in UI symbols to matching accent-colored variants.
/// </summary>
public static class HimoAppearanceManager
{
    private const string ActiveStylePreference = "Himo.ActiveInterfaceStyle";

    private static readonly string[] ThemeResourceNames =
    {
        "HimoBackground", "HimoPrimary", "HimoMuted", "HimoCard", "HimoBorder",
        "HimoInput", "HimoAccent", "HimoAccentDark", "HimoBubbleMine", "HimoBubbleOther",
        "HimoAvatar", "HimoSoft", "HimoHeader", "HimoSurfaceElevated", "HimoDivider",
        "HimoSurface", "HimoHeaderSoft", "HimoHeaderTextMuted", "HimoTextSecondary",
        "HimoAvatarSoft", "HimoNavMuted", "HimoPanel", "HimoPanelRaised",
        "HimoFocus", "HimoGlow", "HimoGlowSoft", "HimoNeonBorder",
        "HimoOnAccent", "HimoIconTint", "HimoIconBackground",
        "HimoSuccess", "HimoDanger", "HimoWarning", "HimoDangerSoft"
    };

    private static readonly Dictionary<string, Palette> Palettes = new(StringComparer.Ordinal)
    {
        ["d_classic"] = new("#F7F4FF", "#6C25D9", "#291841", false),
        ["d_sky"] = new("#EEF7FF", "#287AC4", "#163754", false),
        ["d_light"] = new("#FFFFFF", "#6B47B5", "#282238", false),
        ["d_graphite"] = new("#ECEEF3", "#525D71", "#222936", false),
        ["d_mint"] = new("#ECFAF5", "#1A927A", "#17483F", false),
        ["d_login"] = new("#050A24", "#A56BFF", "#F8FAFF", true),
        ["d_rose"] = new("#FFF1F6", "#D75E93", "#57283D", false),
        ["p_royal_gold"] = new("#17151B", "#F4C66B", "#FFF5DC", true),
        ["p_electric_blue"] = new("#101D34", "#59C8FF", "#EAF7FF", true),
        ["p_neon_violet"] = new("#211333", "#C76BFF", "#F7E9FF", true),
        ["p_emerald"] = new("#102923", "#5AE1B0", "#E5FFF5", true),
        ["p_ruby"] = new("#2A1420", "#FF7097", "#FFEAF0", true),
        ["p_silver"] = new("#252A34", "#D6DEEA", "#F4F6FB", true),
        ["p_aurora"] = new("#102B3A", "#48DFD4", "#E7FFFF", true),
        ["p_rose_gold"] = new("#2C1B22", "#EBA58E", "#FFF0E9", true)
    };

    private static readonly ConditionalWeakTable<VisualElement, OriginalViewColors> OriginalColors = new();
    private static readonly ConditionalWeakTable<VisualElement, AppliedAppearanceState> AppliedAppearances = new();
    private static readonly ConditionalWeakTable<VisualElement, ScheduledAppearanceApply> ScheduledApplies = new();
    private static readonly ConditionalWeakTable<VisualElement, RootAppearanceState> RootAppearanceStates = new();

    private static readonly HashSet<string> UiIconPrefixes = new(StringComparer.Ordinal)
    {
        "himo_pro_ui_", "settings_icon_", "selection_", "himo_icon_", "himo_ui_", "himo_phase1_icon_", "himo_call_", "icon_",
        "himo_login_icon_", "himo_login_eye_", "himo_signup_icon_", "himo_storage_icon_", "himo_download_"
    };

    // Preserve truly semantic delete/end-call art; ordinary white controls continue to use
    // their palette-specific variants so they remain legible as the interface changes theme.
    private static readonly HashSet<string> FixedMeaningIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        "settings_icon_delete.png", "selection_delete.png", "himo_ui_delete.png",
        "himo_icon_call_end.png", "himo_phase1_icon_call_end.png", "himo_call_end.png"
    };

    // Two white source icons have no exported themed variant with the same filename.
    // Map them to their existing palette-aware siblings so MAUI never receives a missing
    // ImageSource (which renders as a completely invisible symbol).
    private static readonly Dictionary<string, string> ThemedIconFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["himo_icon_chat_play_white.png"] = "himo_icon_chat_play.png",
        ["himo_pro_ui_himo_ui_gallery_white.png"] = "himo_pro_ui_himo_ui_gallery.png"
    };

    // Keep the app's original dark-purple feel on clean installs, while making the
    // selected theme available to the gallery before the user opens settings.
    public static string ActiveStyleId => Preferences.Default.Get(ActiveStylePreference, "p_neon_violet");

    public static bool IsActiveStyleDark =>
        Palettes.TryGetValue(ActiveStyleId, out var palette) && palette.IsDark;

    /// <summary>Switch between a light/dark counterpart while preserving the closest accent family.</summary>
    public static void SetDarkModeEnabled(bool enabled)
    {
        var current = ActiveStyleId;
        var target = enabled
            ? current switch
            {
                "d_classic" => "p_neon_violet",
                "d_sky" => "p_electric_blue",
                "d_light" or "d_graphite" => "p_silver",
                "d_mint" => "p_emerald",
                "d_rose" => "p_ruby",
                "d_login" => "d_login",
                _ => current
            }
            : current switch
            {
                "p_royal_gold" => "d_classic",
                "p_electric_blue" => "d_sky",
                "p_neon_violet" => "d_classic",
                "p_emerald" => "d_mint",
                "p_ruby" => "d_rose",
                "p_silver" => "d_graphite",
                "p_aurora" => "d_sky",
                "p_rose_gold" => "d_rose",
                "d_login" => "d_classic",
                _ => current
            };

        if (!Palettes.ContainsKey(target))
            target = enabled ? "p_neon_violet" : "d_classic";

        SetActiveStyle(target);
        Preferences.Default.Set("himo_dark_mode", IsActiveStyleDark);
    }

    /// <summary>Called after the app's baseline resources are initialized.</summary>
    public static void ApplyStoredPalette(ResourceDictionary resources)
    {
        var styleId = ActiveStyleId;
        if (Palettes.TryGetValue(styleId, out var palette))
        {
            ApplyPalette(resources, palette);
            Preferences.Default.Set("himo_dark_mode", palette.IsDark);
            // Himo themes are applied through explicit palette resources. Do not change
            // Application.UserAppTheme here: on Android this calls AppCompat's native
            // configuration path while the activity/component may still be starting,
            // which can recreate the activity and interrupt the current page.
        }
    }

    /// <summary>Persist and apply a selection from either interface gallery.</summary>
    public static void SetActiveStyle(string styleId)
    {
        if (!Palettes.ContainsKey(styleId))
            return;

        Preferences.Default.Set(ActiveStylePreference, styleId);

        void Apply()
        {
            var app = Application.Current;
            if (app is null || !Palettes.TryGetValue(styleId, out var palette))
                return;

            Preferences.Default.Set("himo_dark_mode", palette.IsDark);
            // Apply the full Himo palette without toggling MAUI's native night mode.
            // Switching UserAppTheme during a live Android interaction can invoke
            // AppCompatDelegate configuration changes and drop the active page.
            ApplyPalette(app.Resources, palette);
            // Do not rescan every page during the theme mutation. The page that
            // owns the picker is refreshed by its caller; the rest refresh on navigation.
        }

        if (MainThread.IsMainThread)
            Apply();
        else
            MainThread.BeginInvokeOnMainThread(Apply);
    }

    /// <summary>Refreshes current pages after resource changes or navigation.</summary>
    public static void ApplyIconsToCurrentPages()
    {
        var app = Application.Current;
        var styleId = ActiveStyleId;
        if (app is null || !Palettes.TryGetValue(styleId, out var palette))
            return;

        foreach (var window in app.Windows)
        {
            var page = window.Page;
            if (page is Shell shell)
                page = shell.CurrentPage;
            else if (page is NavigationPage navigationPage)
                page = navigationPage.CurrentPage;

            // Updating hundreds of Image/ImageButton sources synchronously can block
            // Android's UI thread and make a theme/icon switch look like an app exit.
            // The batched pass checks that the page is still active between batches.
            if (page is not null)
                _ = ApplyToTreeAfterFirstFrameAsync(page, batchSize: 16);
        }
    }

    /// <summary>Call from a page's OnAppearing for pages pushed outside Shell routes.</summary>
    public static void ApplyToTree(VisualElement root)
    {
        var styleId = ActiveStyleId;
        if (Palettes.TryGetValue(styleId, out var palette))
            ApplyToTree(root, styleId, palette);
    }

    /// <summary>
    /// Applies only palette-aware image sources, without recoloring the page's controls.
    /// This is used by live camera surfaces where changing capture-control backgrounds
    /// must not alter the camera preview itself.
    /// </summary>
    public static void ApplyThemedIconSourcesToTree(VisualElement root)
    {
        if (root is null)
            return;

        var iconStyleId = ResolveIconStyleId(ActiveStyleId);
        foreach (var view in EnumerateVisualTree(root))
            ApplyIconSource(view, iconStyleId);
    }

    /// <summary>Refreshes a single icon after its ImageSource is changed at runtime.</summary>
    public static void ApplyThemedIconSource(VisualElement view)
    {
        if (view is null)
            return;

        ApplyIconSource(view, ResolveIconStyleId(ActiveStyleId));
    }

    /// <summary>
    /// Applies legacy hard-coded colors after the first frame has had a chance to render.
    /// This keeps a large settings/home XAML tree from blocking the first visible frame.
    /// Repeated OnAppearing calls supersede older scheduled passes for the same page.
    /// </summary>
    public static Task ApplyToTreeAfterFirstFrameAsync(VisualElement root, bool skipCollectionViewContents = false, int batchSize = 24)
    {
        if (batchSize < 1) batchSize = 24;
        var styleId = ActiveStyleId;
        var rootState = RootAppearanceStates.GetValue(root, _ => new RootAppearanceState());
        lock (rootState.Sync)
        {
            // Cached Settings/Profile pages already have their icon/color pass applied.
            // Avoid rescanning hundreds of elements on every visit unless the style changed.
            if (string.Equals(rootState.AppliedStyleId, styleId, StringComparison.Ordinal))
                return Task.CompletedTask;
        }

        var scheduled = ScheduledApplies.GetValue(root, _ => new ScheduledAppearanceApply());
        var generation = Interlocked.Increment(ref scheduled.Generation);
        return ApplyToTreeAfterFirstFrameCoreAsync(root, scheduled, generation, skipCollectionViewContents, batchSize);
    }

    private static async Task ApplyToTreeAfterFirstFrameCoreAsync(
        VisualElement root,
        ScheduledAppearanceApply scheduled,
        int generation,
        bool skipCollectionViewContents,
        int batchSize)
    {
        try
        {
            // Let the page's entrance transition complete before the more expensive
            // legacy-color/icon pass starts. Large Settings trees used to be scanned
            // in one UI-thread block 90 ms after appearing, which interrupted motion.
            var ready = false;
            for (var attempt = 0; attempt < 4 && !ready; attempt++)
            {
                await Task.Delay(attempt == 0 ? 155 : 100).ConfigureAwait(false);
                if (Volatile.Read(ref scheduled.Generation) != generation)
                    return;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ready = Volatile.Read(ref scheduled.Generation) == generation && root.Handler is not null;
                }).ConfigureAwait(false);
            }

            if (!ready || Volatile.Read(ref scheduled.Generation) != generation)
                return;

            // Apply in small batches. Each batch runs on the UI thread because MAUI
            // visual elements must not be read or mutated from a worker thread; the
            // short yield between batches gives Android time to render frames.
            using var iterator = EnumerateVisualTree(root, skipCollectionViewContents).GetEnumerator();
            var finished = false;
            while (!finished)
            {
                var batch = new List<VisualElement>(batchSize);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (Volatile.Read(ref scheduled.Generation) != generation)
                    {
                        finished = true;
                        return;
                    }

                    while (batch.Count < batchSize && iterator.MoveNext())
                        batch.Add(iterator.Current);

                    var styleId = ActiveStyleId;
                    if (!Palettes.TryGetValue(styleId, out var palette))
                    {
                        finished = true;
                        return;
                    }

                    foreach (var view in batch)
                    {
                        if (IsInThemePreview(view))
                            continue;
                        ApplyToView(view, styleId, palette);
                    }

                    finished = batch.Count == 0;
                }).ConfigureAwait(false);

                if (!finished)
                    await Task.Delay(16).ConfigureAwait(false);
            }

            if (Volatile.Read(ref scheduled.Generation) == generation)
            {
                var completedStyleId = ActiveStyleId;
                var completedState = RootAppearanceStates.GetValue(root, _ => new RootAppearanceState());
                lock (completedState.Sync)
                    completedState.AppliedStyleId = completedStyleId;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Appearance] Deferred page styling skipped: {ex.Message}");
        }
    }

    private sealed class ScheduledAppearanceApply
    {
        public int Generation;
    }

    private sealed class RootAppearanceState
    {
        public object Sync { get; } = new();
        public string? AppliedStyleId { get; set; }
    }

    private static void ApplyToTree(VisualElement root, string styleId, Palette palette)
    {
        foreach (var view in EnumerateVisualTree(root))
        {
            if (!IsInThemePreview(view))
                ApplyToView(view, styleId, palette);
        }

        var rootState = RootAppearanceStates.GetValue(root, _ => new RootAppearanceState());
        lock (rootState.Sync)
            rootState.AppliedStyleId = styleId;
    }

    private static void ApplyToView(VisualElement view, string styleId, Palette palette)
    {
        var applied = AppliedAppearances.GetValue(view, _ => new AppliedAppearanceState());
        var currentSourceKey = GetImageSourceKey(view);
        var styleChanged = !string.Equals(applied.StyleId, styleId, StringComparison.Ordinal);

        if (styleChanged)
        {
            // Appearance/icon settings use explicit high-contrast colors, independent of the
            // selected app palette. Still update themed image assets inside those pages.
            if (!IsInStableContrastSettings(view))
            {
                var original = Snapshot(view);
                ApplyColorSnapshot(view, original, palette);
            }
            ApplyIconSource(view, styleId);
            applied.StyleId = styleId;
            applied.ImageSourceKey = GetImageSourceKey(view);
        }
        else if (!string.Equals(currentSourceKey, applied.ImageSourceKey, StringComparison.Ordinal))
        {
            // A page can load a new image after appearing (e.g. avatar/icon).
            // Only touch its source when it actually changed, not on every pass.
            ApplyIconSource(view, styleId);
            applied.ImageSourceKey = GetImageSourceKey(view);
        }
    }

    private static string? GetImageSourceKey(VisualElement view) => view switch
    {
        Image image when image.Source is FileImageSource file => "Image:" + file.File,
        ImageButton imageButton when imageButton.Source is FileImageSource file => "ImageButton:" + file.File,
        Button button when button.ImageSource is FileImageSource file => "Button:" + file.File,
        _ => null
    };

    private sealed class AppliedAppearanceState
    {
        public string? StyleId { get; set; }
        public string? ImageSourceKey { get; set; }
    }

    private static IEnumerable<VisualElement> EnumerateVisualTree(VisualElement root, bool skipCollectionViewContents = false)
    {
        // Theme preview cards deliberately contain a separate, candidate-specific palette.
        // Skip the entire subtree so applying a theme does not walk dozens of preview controls.
        if (IsInThemePreview(root))
            yield break;

        yield return root;
        // Keep the CollectionView itself in the pass, but skip the many generated
        // message-cell descendants when requested by ChatPage.
        if (skipCollectionViewContents && root is CollectionView)
            yield break;
        if (root is not IVisualTreeElement visualTree)
            yield break;

        foreach (var child in visualTree.GetVisualChildren())
        {
            if (child is VisualElement visual)
            {
                foreach (var descendant in EnumerateVisualTree(visual, skipCollectionViewContents))
                    yield return descendant;
            }
        }
    }

    private static OriginalViewColors Snapshot(VisualElement view)
    {
        if (OriginalColors.TryGetValue(view, out var existing))
            return existing;

        var resources = Application.Current?.Resources;
        var background = view.BackgroundColor;
        var text = GetTextColor(view);
        var placeholder = GetPlaceholderColor(view);
        var stroke = view is Border border ? (border.Stroke as SolidColorBrush)?.Color : null;
        var borderColor = GetBorderColor(view);
        var boxColor = view is BoxView box ? box.Color : null;
        var controlColor = GetControlColor(view);
        var thumbColor = GetThumbColor(view);
        var progressColor = view is ProgressBar progress ? progress.ProgressColor : null;
        var minimumTrackColor = view is Slider slider ? slider.MinimumTrackColor : null;
        var maximumTrackColor = view is Slider slider2 ? slider2.MaximumTrackColor : null;

        var snapshot = new OriginalViewColors(
            new ColorSlot(background, IsResourceColor(resources, background)),
            new ColorSlot(text, IsResourceColor(resources, text)),
            new ColorSlot(placeholder, IsResourceColor(resources, placeholder)),
            new ColorSlot(stroke, IsResourceColor(resources, stroke)),
            new ColorSlot(borderColor, IsResourceColor(resources, borderColor)),
            new ColorSlot(boxColor, IsResourceColor(resources, boxColor)),
            new ColorSlot(controlColor, IsResourceColor(resources, controlColor)),
            new ColorSlot(thumbColor, IsResourceColor(resources, thumbColor)),
            new ColorSlot(progressColor, IsResourceColor(resources, progressColor)),
            new ColorSlot(minimumTrackColor, IsResourceColor(resources, minimumTrackColor)),
            new ColorSlot(maximumTrackColor, IsResourceColor(resources, maximumTrackColor)));

        OriginalColors.Add(view, snapshot);
        return snapshot;
    }

    private static void ApplyColorSnapshot(VisualElement view, OriginalViewColors original, Palette palette)
    {
        if (!original.Background.IsResourceBound && original.Background.Value is { } background)
            view.BackgroundColor = MapColor(background, ColorRole.Background, palette);

        if (!original.Text.IsResourceBound && original.Text.Value is { } text)
            SetTextColor(view, MapColor(text, ColorRole.Text, palette));

        if (!original.Placeholder.IsResourceBound && original.Placeholder.Value is { } placeholder)
            SetPlaceholderColor(view, MapColor(placeholder, ColorRole.SecondaryText, palette));

        if (!original.Stroke.IsResourceBound && original.Stroke.Value is { } stroke && view is Border border)
            border.Stroke = new SolidColorBrush(MapColor(stroke, ColorRole.Stroke, palette));

        if (!original.BorderColor.IsResourceBound && original.BorderColor.Value is { } borderColor)
            SetBorderColor(view, MapColor(borderColor, ColorRole.Stroke, palette));

        if (!original.BoxColor.IsResourceBound && original.BoxColor.Value is { } boxColor && view is BoxView box)
            box.Color = MapColor(boxColor, ColorRole.Stroke, palette);

        if (!original.ControlColor.IsResourceBound && original.ControlColor.Value is { } controlColor)
            SetControlColor(view, MapColor(controlColor, ColorRole.Accent, palette));

        if (!original.ThumbColor.IsResourceBound && original.ThumbColor.Value is { } thumbColor)
            SetThumbColor(view, MapColor(thumbColor, ColorRole.Accent, palette));

        if (!original.ProgressColor.IsResourceBound && original.ProgressColor.Value is { } progressColor && view is ProgressBar progress)
            progress.ProgressColor = MapColor(progressColor, ColorRole.Accent, palette);

        if (!original.MinimumTrackColor.IsResourceBound && original.MinimumTrackColor.Value is { } minimum && view is Slider slider)
            slider.MinimumTrackColor = MapColor(minimum, ColorRole.Accent, palette);

        if (!original.MaximumTrackColor.IsResourceBound && original.MaximumTrackColor.Value is { } maximum && view is Slider slider2)
            slider2.MaximumTrackColor = MapColor(maximum, ColorRole.Stroke, palette);

        // Improve text contrast only when the mapped foreground is too close to its
        // nearest opaque background; leave icon assets and layout untouched.
        EnsureReadableForeground(view, palette);
    }

    private static void EnsureReadableForeground(VisualElement view, Palette palette)
    {
        if (view is not Label && view is not Button)
            return;

        var foreground = GetTextColor(view);
        var background = FindNearestOpaqueBackground(view);
        if (foreground is null || background is null || ContrastRatio(foreground, background) >= 4.5)
            return;

        // These are text colors only; select the one with the best contrast for the
        // actual background instead of blindly reusing the active accent color.
        var candidates = new[] { palette.Foreground, palette.Muted, Colors.White, Colors.Black };
        var best = candidates[0];
        var bestContrast = ContrastRatio(best, background);
        for (var i = 1; i < candidates.Length; i++)
        {
            var contrast = ContrastRatio(candidates[i], background);
            if (contrast > bestContrast)
            {
                best = candidates[i];
                bestContrast = contrast;
            }
        }

        SetTextColor(view, best);
    }

    private static Color? FindNearestOpaqueBackground(VisualElement view)
    {
        Element? current = view;
        while (current is not null)
        {
            if (current is VisualElement visual)
            {
                var backgroundColor = visual.BackgroundColor;
                if (backgroundColor is Color solidColor && solidColor.Alpha >= 0.92f)
                    return solidColor;

                // SolidColorBrush.Color can be null while MAUI is constructing or
                // restyling a view. Never dereference it before checking the value.
                if (visual.Background is SolidColorBrush solidBrush &&
                    solidBrush.Color is { } brushColor && brushColor.Alpha >= 0.92f)
                    return brushColor;
            }

            current = current.Parent;
        }

        return null;
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var first = RelativeLuminanceForContrast(foreground);
        var second = RelativeLuminanceForContrast(background);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminanceForContrast(Color color)
    {
        static double Linearize(float channel) => channel <= 0.04045f
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);

        return 0.2126 * Linearize(color.Red) +
               0.7152 * Linearize(color.Green) +
               0.0722 * Linearize(color.Blue);
    }

    // Use the active style for the in-app icon artwork. The previous implementation
    // forced every UI icon to p_electric_blue, so changing a theme appeared to work
    // on the cards but never changed the icons. The login-matching style has no
    // dedicated icon export, so it deliberately reuses the neon-violet icon set.
    /// <summary>The palette that has a matching generated icon set for the active interface.</summary>
    public static string ActiveIconStyleId => ResolveIconStyleId(ActiveStyleId);

    private static string ResolveIconStyleId(string styleId) =>
        string.Equals(styleId, "d_login", StringComparison.Ordinal) ? "p_neon_violet" : styleId;

    private static void ApplyIconSource(VisualElement view, string styleId)
    {
        var iconStyleId = ResolveIconStyleId(styleId);
        if (view is Image image && TryGetThemedSource(image.Source, iconStyleId, out var imageSource))
            image.Source = imageSource;
        else if (view is ImageButton imageButton && TryGetThemedSource(imageButton.Source, iconStyleId, out var imageButtonSource))
            imageButton.Source = imageButtonSource;
        else if (view is Button button && TryGetThemedSource(button.ImageSource, iconStyleId, out var buttonSource))
            button.ImageSource = buttonSource;
    }

    private static bool TryGetThemedSource(ImageSource? source, string styleId, out ImageSource themedSource)
    {
        themedSource = source ?? ImageSource.FromFile("himo_pro_ui_settings_icon_user.png");
        if (source is not FileImageSource fileSource || string.IsNullOrWhiteSpace(fileSource.File))
            return false;

        var file = Path.GetFileName(fileSource.File);
        // Theme variants are generated as PNGs; keep vector/SVG assets intact.
        if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var candidateId in Palettes.Keys)
        {
            var prefix = $"himo_theme_{candidateId}_";
            if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                file = file[prefix.Length..];
                break;
            }
        }

        if (ThemedIconFallbacks.TryGetValue(file, out var fallbackFile))
            file = fallbackFile;

        if (!ShouldThemeIcon(file))
            return false;
        themedSource = ImageSource.FromFile($"himo_theme_{styleId}_{file}");
        return true;
    }

    private static bool ShouldThemeIcon(string file)
    {
        // Keep semantic white/red controls unchanged; these assets are intentionally
        // high-contrast and do not have generated per-palette variants.
        if (FixedMeaningIcons.Contains(file))
            return false;

        if (file.StartsWith("himo_professional_icon_", StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith("himo_system_icon_", StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith("himo_glass_icon_", StringComparison.OrdinalIgnoreCase))
            return false;

        if (file.StartsWith("home_", StringComparison.OrdinalIgnoreCase) && file.EndsWith("_neon.png", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var prefix in UiIconPrefixes)
            if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsInStableContrastSettings(VisualElement view)
    {
        Element? current = view;
        while (current is not null)
        {
            if (string.Equals(current.AutomationId, "HimoSettingsContrastStable", StringComparison.Ordinal))
                return true;
            current = current.Parent;
        }
        return false;
    }

    private static bool IsInThemePreview(VisualElement view)
    {
        Element? current = view;
        while (current is not null)
        {
            if (string.Equals(current.AutomationId, "HimoThemePreview", StringComparison.Ordinal))
                return true;
            current = current.Parent;
        }
        return false;
    }

    private static Color MapColor(Color color, ColorRole role, Palette palette)
    {
        if (color.Alpha < 0.08f || IsSemanticColor(color))
            return color;

        var r = color.Red;
        var g = color.Green;
        var b = color.Blue;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var saturation = max <= 0.001f ? 0f : (max - min) / max;
        var luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        var nearWhite = min > 0.91f;
        var nearBlack = max < 0.10f;
        var purpleBlueBrand = IsPurpleBlueBrandColor(color);

        return role switch
        {
            ColorRole.Background => MapBackgroundColor(color, luminance, saturation, nearWhite, nearBlack, purpleBlueBrand, palette),
            ColorRole.Text => MapTextColor(color, luminance, saturation, nearWhite, purpleBlueBrand, palette),
            ColorRole.SecondaryText => MapSecondaryTextColor(color, luminance, saturation, nearWhite, palette),
            ColorRole.Stroke => MapStrokeColor(color, luminance, saturation, nearWhite, nearBlack, purpleBlueBrand, palette),
            _ => MapAccentColor(color, luminance, saturation, purpleBlueBrand, palette)
        };
    }

    private static Color MapBackgroundColor(Color original, float luminance, float saturation, bool nearWhite, bool nearBlack, bool brand, Palette palette)
    {
        if (nearWhite)
            return luminance > 0.975f ? palette.Card : Blend(palette.Card, palette.Soft, 0.25f);
        if (nearBlack || luminance < 0.065f)
            return palette.Background;
        if (luminance < 0.145f)
            return palette.Card;
        if (luminance < 0.24f)
            return palette.Surface;
        if (brand && saturation > 0.30f && luminance > 0.25f)
            return palette.Accent;
        if (saturation > 0.52f && luminance > 0.24f)
            return palette.Accent;
        if (luminance < 0.37f)
            return palette.Soft;
        if (luminance > 0.78f)
            return Blend(palette.Card, palette.Soft, 0.18f);
        return palette.Surface;
    }

    private static Color MapTextColor(Color original, float luminance, float saturation, bool nearWhite, bool brand, Palette palette)
    {
        if (nearWhite || luminance < 0.28f)
            return palette.Foreground;
        if (brand || (saturation > 0.48f && luminance < 0.80f))
            return palette.Accent;
        if (luminance > 0.78f && saturation < 0.25f)
            return palette.Foreground;
        return palette.Muted;
    }

    private static Color MapSecondaryTextColor(Color original, float luminance, float saturation, bool nearWhite, Palette palette)
    {
        if (nearWhite || luminance < 0.25f)
            return palette.Foreground;
        if (IsPurpleBlueBrandColor(original) && saturation > 0.48f)
            return palette.Accent;
        return palette.Muted;
    }

    private static Color MapStrokeColor(Color original, float luminance, float saturation, bool nearWhite, bool nearBlack, bool brand, Palette palette)
    {
        if (nearBlack || luminance < 0.10f)
            return palette.Border;
        if (nearWhite || (brand && saturation < 0.55f))
            return palette.Border;
        if (brand || saturation > 0.48f)
            return palette.Accent;
        if (luminance < 0.30f)
            return palette.Border;
        return Blend(palette.Border, palette.Accent, 0.3f);
    }

    private static Color MapAccentColor(Color original, float luminance, float saturation, bool brand, Palette palette)
    {
        if (luminance < 0.18f && saturation < 0.55f)
            return palette.Border;
        if (brand || saturation > 0.36f)
            return palette.Accent;
        return palette.Muted;
    }

    private static bool IsPurpleBlueBrandColor(Color color)
    {
        var r = color.Red;
        var g = color.Green;
        var b = color.Blue;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max < 0.08f || max - min < 0.07f)
            return false;
        // Himo's existing palette uses violet, blue-violet and cyan accents.
        return (b > g * 1.12f && r > g * 1.03f) || (b > r * 1.25f && b > g * 1.20f);
    }

    private static bool IsSemanticColor(Color color)
    {
        var r = color.Red;
        var g = color.Green;
        var b = color.Blue;
        // Keep traffic-light meaning: danger/red, success/green and warning/orange.
        var redOrPink = r > 0.56f && r > g * 1.24f && b < r * 0.80f;
        var green = g > 0.42f && g > r * 1.20f && g > b * 1.12f;
        var orange = r > 0.65f && g > 0.32f && b < g * 0.58f;
        return redOrPink || green || orange;
    }

    private static bool IsResourceColor(ResourceDictionary? resources, Color? value)
    {
        if (resources is null || value is null)
            return false;
        foreach (var key in ThemeResourceNames)
        {
            if (resources.TryGetValue(key, out var item) && item is Color resourceColor && ColorsEqual(resourceColor, value))
                return true;
        }
        return false;
    }

    private static bool ColorsEqual(Color left, Color right) =>
        Math.Abs(left.Red - right.Red) < 0.002f && Math.Abs(left.Green - right.Green) < 0.002f &&
        Math.Abs(left.Blue - right.Blue) < 0.002f && Math.Abs(left.Alpha - right.Alpha) < 0.002f;

    private static Color Blend(Color first, Color second, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color(
            first.Red + ((second.Red - first.Red) * amount),
            first.Green + ((second.Green - first.Green) * amount),
            first.Blue + ((second.Blue - first.Blue) * amount),
            first.Alpha + ((second.Alpha - first.Alpha) * amount));
    }

    private static void ApplyPalette(ResourceDictionary resources, Palette palette)
    {
        var background = palette.Background;
        var foreground = palette.Foreground;
        var accent = palette.Accent;
        var card = palette.Card;
        var soft = palette.Soft;
        var surface = palette.Surface;
        var muted = palette.Muted;
        var border = palette.Border;

        resources["HimoBackground"] = background;
        resources["HimoPrimary"] = foreground;
        resources["HimoMuted"] = muted;
        resources["HimoCard"] = card;
        resources["HimoInput"] = surface;
        resources["HimoAccent"] = accent;
        resources["HimoAccentDark"] = Blend(accent, Colors.Black, 0.30f);
        resources["HimoBorder"] = border;
        resources["HimoBubbleMine"] = accent;
        resources["HimoBubbleOther"] = soft;
        resources["HimoAvatar"] = surface;
        resources["HimoSoft"] = soft;
        resources["HimoHeader"] = background;
        resources["HimoHeaderSoft"] = surface;
        resources["HimoHeaderTextMuted"] = muted;
        resources["HimoTextSecondary"] = muted;
        resources["HimoAvatarSoft"] = soft;
        resources["HimoNavMuted"] = muted;
        resources["HimoSurfaceElevated"] = surface;
        resources["HimoSurface"] = surface;
        resources["HimoDivider"] = border;
        resources["HimoFocus"] = Blend(accent, Colors.White, palette.IsDark ? 0.25f : 0.12f);
        resources["HimoGlow"] = accent;
        resources["HimoGlowSoft"] = Blend(accent, background, 0.42f);
        resources["HimoPanel"] = card;
        resources["HimoPanelRaised"] = surface;
        resources["HimoNeonBorder"] = border;
        resources["HimoIconTint"] = accent;
        resources["HimoIconBackground"] = soft;
        resources["HimoOnAccent"] = RelativeLuminance(accent) > 0.55f ? Color.FromArgb("#21172B") : Colors.White;

        resources["HimoBackgroundGradient"] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(background, 0f),
                new GradientStop(surface, 0.55f),
                new GradientStop(Blend(background, accent, 0.12f), 1f)
            }
        };
        resources["HimoAccentGradient"] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Blend(accent, Colors.White, 0.22f), 0f),
                new GradientStop(accent, 0.55f),
                new GradientStop(Blend(accent, background, 0.12f), 1f)
            }
        };
    }

    private static float RelativeLuminance(Color color) => 0.2126f * color.Red + 0.7152f * color.Green + 0.0722f * color.Blue;

    private static Color? GetTextColor(VisualElement view) => view switch
    {
        Label label => label.TextColor,
        Button button => button.TextColor,
        Entry entry => entry.TextColor,
        Editor editor => editor.TextColor,
        SearchBar searchBar => searchBar.TextColor,
        Picker picker => picker.TextColor,
        DatePicker datePicker => datePicker.TextColor,
        TimePicker timePicker => timePicker.TextColor,
        CheckBox checkBox => checkBox.Color,
        _ => null
    };

    private static void SetTextColor(VisualElement view, Color value)
    {
        switch (view)
        {
            case Label label: label.TextColor = value; break;
            case Button button: button.TextColor = value; break;
            case Entry entry: entry.TextColor = value; break;
            case Editor editor: editor.TextColor = value; break;
            case SearchBar searchBar: searchBar.TextColor = value; break;
            case Picker picker: picker.TextColor = value; break;
            case DatePicker datePicker: datePicker.TextColor = value; break;
            case TimePicker timePicker: timePicker.TextColor = value; break;
            case CheckBox checkBox: checkBox.Color = value; break;
        }
    }

    private static Color? GetPlaceholderColor(VisualElement view) => view switch
    {
        Entry entry => entry.PlaceholderColor,
        Editor editor => editor.PlaceholderColor,
        SearchBar searchBar => searchBar.PlaceholderColor,
        Picker picker => picker.TitleColor,
        _ => null
    };

    private static void SetPlaceholderColor(VisualElement view, Color value)
    {
        switch (view)
        {
            case Entry entry: entry.PlaceholderColor = value; break;
            case Editor editor: editor.PlaceholderColor = value; break;
            case SearchBar searchBar: searchBar.PlaceholderColor = value; break;
            case Picker picker: picker.TitleColor = value; break;
        }
    }

    private static Color? GetBorderColor(VisualElement view) => view switch
    {
        Button button => button.BorderColor,
        ImageButton imageButton => imageButton.BorderColor,
        _ => null
    };

    private static void SetBorderColor(VisualElement view, Color value)
    {
        switch (view)
        {
            case Button button: button.BorderColor = value; break;
            case ImageButton imageButton: imageButton.BorderColor = value; break;
        }
    }

    private static Color? GetControlColor(VisualElement view) => view switch
    {
        ActivityIndicator indicator => indicator.Color,
        Switch toggle => toggle.OnColor,
        ProgressBar progress => progress.ProgressColor,
        CheckBox checkBox => checkBox.Color,
        _ => null
    };

    private static void SetControlColor(VisualElement view, Color value)
    {
        switch (view)
        {
            case ActivityIndicator indicator: indicator.Color = value; break;
            case Switch toggle: toggle.OnColor = value; break;
            case ProgressBar progress: progress.ProgressColor = value; break;
            case CheckBox checkBox: checkBox.Color = value; break;
        }
    }

    private static Color? GetThumbColor(VisualElement view) => view switch
    {
        Switch toggle => toggle.ThumbColor,
        Slider slider => slider.ThumbColor,
        _ => null
    };

    private static void SetThumbColor(VisualElement view, Color value)
    {
        switch (view)
        {
            case Switch toggle: toggle.ThumbColor = value; break;
            case Slider slider: slider.ThumbColor = value; break;
        }
    }

    private enum ColorRole { Background, Text, SecondaryText, Stroke, Accent }

    private readonly record struct ColorSlot(Color? Value, bool IsResourceBound);

    private sealed record OriginalViewColors(
        ColorSlot Background,
        ColorSlot Text,
        ColorSlot Placeholder,
        ColorSlot Stroke,
        ColorSlot BorderColor,
        ColorSlot BoxColor,
        ColorSlot ControlColor,
        ColorSlot ThumbColor,
        ColorSlot ProgressColor,
        ColorSlot MinimumTrackColor,
        ColorSlot MaximumTrackColor);

    private sealed class Palette
    {
        public Palette(string background, string accent, string foreground, bool isDark)
        {
            Background = Color.FromArgb(background);
            Accent = Color.FromArgb(accent);
            Foreground = Color.FromArgb(foreground);
            IsDark = isDark;
            var surfaceMix = isDark ? 0.075f : 0.035f;
            var softMix = isDark ? 0.14f : 0.095f;
            Card = isDark ? Blend(Background, Foreground, 0.07f) : Color.FromArgb("#FFFFFF");
            Surface = Blend(Background, Foreground, surfaceMix);
            Soft = Blend(Background, Accent, softMix);
            Muted = isDark ? Blend(Foreground, Background, 0.34f) : Blend(Foreground, Background, 0.42f);
            Border = Blend(Background, Accent, isDark ? 0.43f : 0.24f);
        }

        public Color Background { get; }
        public Color Accent { get; }
        public Color Foreground { get; }
        public Color Card { get; }
        public Color Surface { get; }
        public Color Soft { get; }
        public Color Muted { get; }
        public Color Border { get; }
        public bool IsDark { get; }
    }
}
