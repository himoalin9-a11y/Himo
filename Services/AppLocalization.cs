using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Himo.Services;

/// <summary>
/// Resource-backed UI localization. Arabic is the source language; translated
/// values are loaded from Resources/Localization/AppStrings.{culture}.resx.
/// Text is translated only when an exact source phrase exists in the resources,
/// so usernames, message contents and arbitrary user input are not rewritten.
/// </summary>
public static class AppLocalization
{
    public const string PreferenceKey = "himo_app_language";
    private const string ResourceBaseName = "Himo.Resources.Localization.AppStrings";
    private static readonly ResourceManager ResourceManager = new(ResourceBaseName, typeof(AppLocalization).Assembly);
    private static readonly Lazy<Dictionary<string, string>> SourceTextToKey = new(BuildSourceTextMap, true);
    private static readonly Lazy<List<SourceTextTemplate>> SourceTextTemplates = new(BuildSourceTextTemplates, true);
    private static readonly ConditionalWeakTable<BindableObject, PropertyTextState> PropertyStates = new();
    private static readonly ConditionalWeakTable<BindableObject, ObservedNodeState> ObservedNodes = new();
    private static readonly ConditionalWeakTable<Page, AttachedPageState> AttachedPages = new();
    private static readonly ConcurrentDictionary<string, string> TranslatedTextToSource = new(StringComparer.Ordinal);
    private static readonly string[] SupportedCodes = { "ar", "en", "fr", "tr", "ur", "es", "id", "de", "pt", "zh-hans", "hi", "ja" };
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    public static event EventHandler? LanguageChanged;

    public static string CurrentLanguageCode => _culture.Name;
    public static bool IsRightToLeft => CurrentLanguageCode.Equals("ar", StringComparison.OrdinalIgnoreCase) || CurrentLanguageCode.Equals("ur", StringComparison.OrdinalIgnoreCase);
    public static FlowDirection PreferredFlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Loads the saved culture before the first application page is shown.</summary>
    public static void InitializeFromPreferences()
    {
        var stored = Preferences.Default.Get(PreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(stored))
        {
            // Migrate the two-language preference written by the earlier language patch.
            // This keeps an already selected English/Arabic setting instead of resetting it.
            stored = Preferences.Default.Get("Himo.AppLanguageCode", "en");
            Preferences.Default.Set(PreferenceKey, NormalizeCultureCode(stored));
        }

        ApplyCulture(NormalizeCultureCode(stored));
    }

    /// <summary>Persists and applies the requested language, then refreshes open UI trees.</summary>
    public static void SetLanguage(string languageCode)
    {
        var normalized = NormalizeCultureCode(languageCode);
        Preferences.Default.Set(PreferenceKey, normalized);
        ApplyCulture(normalized);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);

            var app = Application.Current;
            if (app is null) return;

            foreach (var window in app.Windows)
            {
                if (window.Page is not Page page) continue;
                page.FlowDirection = PreferredFlowDirection;
                if (page is Shell shell)
                {
                    shell.FlowDirection = PreferredFlowDirection;
                    ApplyToShellTitlesOnly(shell);
                    if (shell.CurrentPage is { } shellPage)
                        _ = ApplyToPageInBatchesAsync(shellPage, batchSize: 64, skipCollectionViewContents: true);
                }
                else
                {
                    _ = ApplyToPageInBatchesAsync(page, batchSize: 64, skipCollectionViewContents: true);
                }

                // Modal pages are not descendants of Shell.CurrentPage. Refresh them
                // separately so dialogs/pages opened before a language switch do not
                // retain text in the previous language.
                foreach (var modalPage in page.Navigation.ModalStack)
                    _ = ApplyToPageInBatchesAsync(modalPage, batchSize: 64, skipCollectionViewContents: true);
            }

        });
    }

    public static string T(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        var translated = ResourceManager.GetString(key, _culture) ?? key;
        var source = ResourceManager.GetString(key, CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(source)) RememberTranslatedSource(source, translated);
        return translated;
    }

    /// <summary>Translates a fixed source-language phrase when a resource exists; otherwise returns it unchanged.</summary>
    public static string TranslateSourceText(string? sourceText)
    {
        if (string.IsNullOrEmpty(sourceText)) return sourceText ?? string.Empty;
        var translated = TranslateText(sourceText);
        RememberTranslatedSource(sourceText, translated);
        return translated;
    }

    private static void RememberTranslatedSource(string source, string translated)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translated) ||
            string.Equals(source, translated, StringComparison.Ordinal) ||
            !source.Any(ch => ch is >= '\u0600' and <= '\u06FF')) return;

        var normalizedSource = Regex.Replace(source.Trim(), @"[ \t]+", " ");
        var isKnownSource = SourceTextToKey.Value.ContainsKey(normalizedSource) ||
                            SourceTextTemplates.Value.Any(template => template.Matcher.IsMatch(source.Trim()));
        if (!isKnownSource) return;

        TranslatedTextToSource.AddOrUpdate(
            translated, source,
            (_, existing) => string.Equals(existing, source, StringComparison.Ordinal) ? existing : string.Empty);
    }

    private static string RestoreSourceText(string text)
        => TranslatedTextToSource.TryGetValue(text, out var source) && !string.IsNullOrEmpty(source)
            ? source
            : text;

    /// <summary>Maps a localized action-sheet label back to its stable source value so
    /// existing command handlers and persisted preference keys remain unchanged.</summary>
    public static string ResolveSourceText(string? displayedText, params string[] sourceOptions)
    {
        if (string.IsNullOrEmpty(displayedText)) return displayedText ?? string.Empty;
        foreach (var sourceOption in sourceOptions)
        {
            if (string.Equals(displayedText, TranslateSourceText(sourceOption), StringComparison.Ordinal))
                return sourceOption;
        }
        return displayedText;
    }

    public static void AttachToPage(Page page)
    {
        if (page is null) return;
        if (!AttachedPages.TryGetValue(page, out _))
        {
            AttachedPages.Add(page, new AttachedPageState());
            page.Appearing += (_, _) =>
                _ = ApplyToPageInBatchesAsync(page, batchSize: 64, skipCollectionViewContents: true);
        }

        _ = ApplyToPageInBatchesAsync(page, batchSize: 64, skipCollectionViewContents: true);
    }

    public static void ApplyToShell(Shell shell)
    {
        if (shell is null) return;
        shell.FlowDirection = PreferredFlowDirection;
        TranslateProperty(shell, nameof(Shell.Title), shell.Title, value => shell.Title = value);

        foreach (var item in shell.Items)
        {
            TranslateProperty(item, nameof(ShellItem.Title), item.Title, value => item.Title = value);
            foreach (var section in item.Items)
            {
                TranslateProperty(section, nameof(ShellSection.Title), section.Title, value => section.Title = value);
                foreach (var content in section.Items)
                {
                    TranslateProperty(content, nameof(ShellContent.Title), content.Title, value => content.Title = value);
                }
            }
        }

        if (shell.CurrentPage is { } page)
            ApplyToPage(page);
    }

    public static void ApplyToPage(Page page)
    {
        if (page is null) return;
        page.FlowDirection = PreferredFlowDirection;
        TranslateProperty(page, nameof(Page.Title), page.Title, value => page.Title = value);

        if (page is Shell shell)
            ApplyToShellTitlesOnly(shell);

        foreach (var toolbarItem in page.ToolbarItems)
            TranslateProperty(toolbarItem, nameof(ToolbarItem.Text), toolbarItem.Text, value => toolbarItem.Text = value);

        if (page is not IVisualTreeElement visualRoot) return;
        foreach (var visualNode in visualRoot.GetVisualTreeDescendants())
        {
            if (visualNode is VisualElement visualElement)
                visualElement.FlowDirection = PreferredFlowDirection;

            if (visualNode is BindableObject bindable)
            {
                var allowLabelText = !IsInsideItemsControl(visualNode);
                ObserveTextChanges(bindable, allowLabelText);
                TranslateControl(bindable, allowLabelText);
            }
        }
    }

    /// <summary>
    /// Applies page localization in small UI-thread batches so large pages such as
    /// ChatPage do not block Android rendering while walking their full visual tree.
    /// </summary>
    public static async Task ApplyToPageInBatchesAsync(Page page, int batchSize = 24, bool skipCollectionViewContents = false)
    {
        if (page is null) return;
        if (batchSize < 1) batchSize = 24;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            page.FlowDirection = PreferredFlowDirection;
            TranslateProperty(page, nameof(Page.Title), page.Title, value => page.Title = value);
            if (page is Shell shell)
                ApplyToShellTitlesOnly(shell);
            foreach (var toolbarItem in page.ToolbarItems)
                TranslateProperty(toolbarItem, nameof(ToolbarItem.Text), toolbarItem.Text, value => toolbarItem.Text = value);
        }).ConfigureAwait(false);

        if (page is not IVisualTreeElement visualRoot) return;

        IEnumerator<IVisualTreeElement>? iterator = null;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            iterator = (skipCollectionViewContents
                ? GetVisualTreeDescendantsSkippingCollectionViews(visualRoot)
                : visualRoot.GetVisualTreeDescendants()).GetEnumerator();
        }).ConfigureAwait(false);
        if (iterator is null) return;
        var treeIterator = iterator;

        try
        {
            var finished = false;
            while (!finished)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var batch = new List<IVisualTreeElement>(batchSize);
                    while (batch.Count < batchSize && treeIterator.MoveNext())
                        batch.Add(treeIterator.Current);

                    foreach (var visualNode in batch)
                    {
                        if (visualNode is VisualElement visualElement)
                            visualElement.FlowDirection = PreferredFlowDirection;

                        if (visualNode is BindableObject bindable)
                        {
                            var allowLabelText = !IsInsideItemsControl(visualNode);
                            ObserveTextChanges(bindable, allowLabelText);
                            TranslateControl(bindable, allowLabelText);
                        }
                    }
                    finished = batch.Count == 0;
                }).ConfigureAwait(false);

                if (!finished)
                    await Task.Delay(16).ConfigureAwait(false);
            }
        }
        finally
        {
            treeIterator.Dispose();
        }
    }
    private static IEnumerable<IVisualTreeElement> GetVisualTreeDescendantsSkippingCollectionViews(IVisualTreeElement root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            yield return child;
            // The CollectionView owns virtualized chat-message templates. Their bound
            // message text should not be translated; FlowDirection is inherited from
            // the CollectionView itself, so walking every realized cell is unnecessary.
            if (child is CollectionView)
                continue;

            foreach (var descendant in GetVisualTreeDescendantsSkippingCollectionViews(child))
                yield return descendant;
        }
    }
    /// <summary>
    /// Fast path for Arabic source-language pages: update page-level direction/title
    /// without walking thousands of descendants in a large chat visual tree.
    /// </summary>
    public static void ApplyPageRootOnly(Page page)
    {
        if (page is null) return;
        page.FlowDirection = PreferredFlowDirection;
        TranslateProperty(page, nameof(Page.Title), page.Title, value => page.Title = value);
        foreach (var toolbarItem in page.ToolbarItems)
            TranslateProperty(toolbarItem, nameof(ToolbarItem.Text), toolbarItem.Text, value => toolbarItem.Text = value);
    }
    private static void ApplyToShellTitlesOnly(Shell shell)
    {
        TranslateProperty(shell, nameof(Shell.Title), shell.Title, value => shell.Title = value);
        foreach (var item in shell.Items)
        {
            TranslateProperty(item, nameof(ShellItem.Title), item.Title, value => item.Title = value);
            foreach (var section in item.Items)
            {
                TranslateProperty(section, nameof(ShellSection.Title), section.Title, value => section.Title = value);
                foreach (var content in section.Items)
                    TranslateProperty(content, nameof(ShellContent.Title), content.Title, value => content.Title = value);
            }
        }
    }

    private static void TranslateControl(BindableObject target, bool allowLabelText)
    {
        switch (target)
        {
            case Page page:
                TranslateProperty(page, nameof(Page.Title), page.Title, value => page.Title = value);
                foreach (var toolbarItem in page.ToolbarItems)
                    TranslateProperty(toolbarItem, nameof(ToolbarItem.Text), toolbarItem.Text, value => toolbarItem.Text = value);
                break;
            case Label label:
                // Do not rewrite virtualized list content or labels explicitly marked
                // as user-supplied (for example a public profile name/status).
                if (allowLabelText && !string.Equals(label.AutomationId, "Himo.UserSuppliedText", StringComparison.Ordinal))
                {
                    TranslateProperty(label, nameof(Label.Text), label.Text, value => label.Text = value);
                    if (label.FormattedText is { } formatted)
                    {
                        foreach (var span in formatted.Spans)
                            TranslateProperty(span, nameof(Span.Text), span.Text, value => span.Text = value);
                    }
                }
                break;
            case Button button:
                TranslateProperty(button, nameof(Button.Text), button.Text, value => button.Text = value);
                break;
            case Entry entry:
                TranslateProperty(entry, nameof(Entry.Placeholder), entry.Placeholder, value => entry.Placeholder = value);
                break;
            case SearchBar searchBar:
                TranslateProperty(searchBar, nameof(SearchBar.Placeholder), searchBar.Placeholder, value => searchBar.Placeholder = value);
                break;
            case Editor editor:
                TranslateProperty(editor, nameof(Editor.Placeholder), editor.Placeholder, value => editor.Placeholder = value);
                break;
            case Picker picker:
                // Keep Picker.Items values untouched: some existing selection handlers
                // persist their original Arabic values as domain/preference keys.
                TranslateProperty(picker, nameof(Picker.Title), picker.Title, value => picker.Title = value);
                break;
            case RadioButton radioButton when radioButton.Content is string radioText:
                TranslateProperty(radioButton, nameof(RadioButton.Content), radioText, value => radioButton.Content = value);
                break;
            // ToolbarItem and SwipeItem derive from MenuItem, so handle the
            // more specific types first to keep every switch arm reachable.
            case ToolbarItem toolbarItem:
                TranslateProperty(toolbarItem, nameof(ToolbarItem.Text), toolbarItem.Text, value => toolbarItem.Text = value);
                break;
            case SwipeItem swipeItem:
                TranslateProperty(swipeItem, nameof(SwipeItem.Text), swipeItem.Text, value => swipeItem.Text = value);
                break;
            case MenuItem menuItem:
                TranslateProperty(menuItem, nameof(MenuItem.Text), menuItem.Text, value => menuItem.Text = value);
                break;
            case ShellItem shellItem:
                TranslateProperty(shellItem, nameof(ShellItem.Title), shellItem.Title, value => shellItem.Title = value);
                break;
            case ShellSection shellSection:
                TranslateProperty(shellSection, nameof(ShellSection.Title), shellSection.Title, value => shellSection.Title = value);
                break;
            case ShellContent shellContent:
                TranslateProperty(shellContent, nameof(ShellContent.Title), shellContent.Title, value => shellContent.Title = value);
                break;
        }
    }

    private static void ObserveTextChanges(BindableObject target, bool allowLabelText)
    {
        if (ObservedNodes.TryGetValue(target, out _)) return;

        var state = new ObservedNodeState(allowLabelText);
        ObservedNodes.Add(target, state);
        target.PropertyChanged += (_, args) =>
        {
            if (state.IsApplying || !IsTranslatableTextProperty(target, args.PropertyName)) return;
            state.IsApplying = true;
            try
            {
                TranslateControl(target, state.AllowLabelText);
            }
            finally
            {
                state.IsApplying = false;
            }
        };
    }

    private static bool IsTranslatableTextProperty(BindableObject target, string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return false;
        return target switch
        {
            Page _ => propertyName == nameof(Page.Title),
            Label _ => propertyName is nameof(Label.Text) or nameof(Label.FormattedText),
            Button _ => propertyName == nameof(Button.Text),
            Entry _ => propertyName == nameof(Entry.Placeholder),
            SearchBar _ => propertyName == nameof(SearchBar.Placeholder),
            Editor _ => propertyName == nameof(Editor.Placeholder),
            Picker _ => propertyName == nameof(Picker.Title),
            RadioButton _ => propertyName == nameof(RadioButton.Content),
            // Keep derived MenuItem types before MenuItem itself.
            ToolbarItem _ => propertyName == nameof(ToolbarItem.Text),
            SwipeItem _ => propertyName == nameof(SwipeItem.Text),
            MenuItem _ => propertyName == nameof(MenuItem.Text),
            ShellItem _ => propertyName == nameof(ShellItem.Title),
            ShellSection _ => propertyName == nameof(ShellSection.Title),
            ShellContent _ => propertyName == nameof(ShellContent.Title),
            _ => false
        };
    }

    private static bool IsInsideItemsControl(IVisualTreeElement node)
    {
        for (var parent = node.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            var typeName = parent.GetType().Name;
            if (typeName.Contains("CollectionView", StringComparison.Ordinal) ||
                typeName.Contains("CarouselView", StringComparison.Ordinal) ||
                typeName.Contains("ListView", StringComparison.Ordinal) ||
                typeName.Contains("ItemsView", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void TranslateProperty(BindableObject owner, string propertyName, string? currentValue, Action<string> setter)
    {
        if (string.IsNullOrEmpty(currentValue)) return;

        var state = PropertyStates.GetValue(owner, _ => new PropertyTextState());
        if (!state.Values.TryGetValue(propertyName, out var textState))
        {
            textState = new SingleTextState(RestoreSourceText(currentValue), currentValue);
            state.Values[propertyName] = textState;
        }
        else if (!string.Equals(currentValue, textState.LastApplied, StringComparison.Ordinal))
        {
            // A control was updated by the page's own logic. Treat that new value
            // as the new source phrase instead of restoring stale text later.
            textState.Original = RestoreSourceText(currentValue);
        }

        var translated = TranslateText(textState.Original);
        textState.LastApplied = translated;
        if (!string.Equals(currentValue, translated, StringComparison.Ordinal))
            setter(translated);
    }

    private static string TranslateText(string original)
    {
        var leadingLength = original.Length - original.TrimStart().Length;
        var trailingLength = original.Length - original.TrimEnd().Length;
        var trimmed = original.Trim();
        var leading = leadingLength > 0 ? original[..leadingLength] : string.Empty;
        var trailing = trailingLength > 0 ? original[^trailingLength..] : string.Empty;

        // Keep a selected check mark while translating the fixed status phrase after it.
        var marker = string.Empty;
        var lookupText = trimmed;
        if (lookupText.StartsWith("✓", StringComparison.Ordinal))
        {
            var index = 1;
            while (index < lookupText.Length && char.IsWhiteSpace(lookupText[index])) index++;
            marker = lookupText[..index];
            lookupText = lookupText[index..];
        }

        var normalizedLookup = Regex.Replace(lookupText, @"[ \t]+", " ").Trim();
        if (SourceTextToKey.Value.TryGetValue(normalizedLookup, out var key))
        {
            var translated = ResourceManager.GetString(key, _culture) ?? normalizedLookup;
            return leading + marker + translated + trailing;
        }

        // Resource templates preserve values such as counts, usernames, elapsed time
        // and exception details while translating the surrounding fixed sentence.
        foreach (var template in SourceTextTemplates.Value)
        {
            var match = template.Matcher.Match(lookupText);
            if (!match.Success) continue;

            var translatedTemplate = ResourceManager.GetString(template.Key, _culture) ?? template.Source;
            for (var i = 0; i < template.Placeholders.Length; i++)
                translatedTemplate = translatedTemplate.Replace(template.Placeholders[i], match.Groups[i + 1].Value, StringComparison.Ordinal);
            return leading + marker + translatedTemplate + trailing;
        }

        // Some UI values append dynamic data (email address, username or exception text)
        // to a fixed resource-backed prefix. Translate only known colon/@ prefixes and
        // preserve the dynamic suffix exactly as supplied.
        var prefixMatch = SourceTextToKey.Value
            .Where(pair =>
            {
                if (trimmed.Length <= pair.Key.Length || !trimmed.StartsWith(pair.Key, StringComparison.Ordinal))
                    return false;
                var suffix = trimmed[pair.Key.Length..];
                return pair.Key.EndsWith(':') || pair.Key.EndsWith('@') ||
                       ((pair.Key is "مراسلة" or "إزالة") && suffix.StartsWith(" ", StringComparison.Ordinal)) ||
                       suffix.StartsWith("\n", StringComparison.Ordinal) ||
                       suffix.StartsWith("\r\n", StringComparison.Ordinal) ||
                       suffix.StartsWith(" •", StringComparison.Ordinal);
            })
            .OrderByDescending(pair => pair.Key.Length)
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(prefixMatch.Key))
        {
            var translatedPrefix = ResourceManager.GetString(prefixMatch.Value, _culture) ?? prefixMatch.Key;
            var suffix = trimmed[prefixMatch.Key.Length..];
            return leading + marker + translatedPrefix + suffix + trailing;
        }

        return original;
    }


    private static List<SourceTextTemplate> BuildSourceTextTemplates()
    {
        var templates = new List<SourceTextTemplate>();
        // ResourceManager owns and caches this ResourceSet. Do not dispose it here;
        // another localization cache may enumerate the same instance later.
        var resourceSet = ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        if (resourceSet is null) return templates;

        foreach (var entry in resourceSet.Cast<DictionaryEntry>())
        {
            if (entry.Key is not string key || entry.Value is not string source || string.IsNullOrWhiteSpace(source))
                continue;
            if (!source.Any(ch => ch is >= '\u0600' and <= '\u06FF'))
                continue;

            var matches = Regex.Matches(source, @"\{[^{}]+\}");
            if (matches.Count == 0) continue;

            var pattern = "^";
            var position = 0;
            var placeholders = new string[matches.Count];
            for (var i = 0; i < matches.Count; i++)
            {
                var placeholder = matches[i];
                pattern += Regex.Escape(source[position..placeholder.Index]) + "([\\s\\S]*?)";
                placeholders[i] = placeholder.Value;
                position = placeholder.Index + placeholder.Length;
            }
            pattern += Regex.Escape(source[position..]) + "$";
            try
            {
                templates.Add(new SourceTextTemplate(key, source, new Regex(pattern, RegexOptions.CultureInvariant), placeholders));
            }
            catch (ArgumentException)
            {
                // Invalid source templates are ignored; exact phrase translation remains available.
            }
        }

        return templates.OrderByDescending(item => item.Source.Length).ToList();
    }

    private static Dictionary<string, string> BuildSourceTextMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        // ResourceManager owns and caches this ResourceSet. Do not dispose it here;
        // another localization cache may enumerate the same instance later.
        var resourceSet = ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        if (resourceSet is null) return map;
        foreach (DictionaryEntry entry in resourceSet)
        {
            // Pattern matching both validates the resource item and gives the
            // compiler non-null string values without nullable casts.
            if (entry.Key is not string key || entry.Value is not string value)
                continue;

            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Avoid translating brand names or non-Arabic tokens; resources use Arabic as the source.
            if (!value.Any(ch => ch is >= '\u0600' and <= '\u06FF'))
                continue;

            var trimmed = value.Trim();
            map.TryAdd(trimmed, key);

            // Many visual themes and attachment previews prefix a translatable
            // phrase with an emoji. Keep the same resource key available when a
            // page displays the plain text without that decorative marker.
            var plainAlias = Regex.Replace(trimmed, @"^(?:[\p{So}\p{Sk}\p{Sm}]|\s)+", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(plainAlias))
                map.TryAdd(plainAlias, key);

            // A few UI surfaces omit the leading verb while preserving the same
            // fixed phrase (for example, "محادثة جديدة").
            if (trimmed.StartsWith("بدء ", StringComparison.Ordinal))
                map.TryAdd(trimmed[4..], key);
        }
        return map;
    }

    private static void ApplyCulture(string languageCode)
    {
        try
        {
            _culture = CultureInfo.GetCultureInfo(languageCode);
        }
        catch (CultureNotFoundException)
        {
            _culture = CultureInfo.GetCultureInfo("en");
        }

        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
    }

    private static string NormalizeCultureCode(string? code)
    {
        var normalized = (code ?? "en").Trim().ToLowerInvariant();
        return SupportedCodes.Contains(normalized, StringComparer.Ordinal) ? normalized : "en";
    }

    private sealed record SourceTextTemplate(string Key, string Source, Regex Matcher, string[] Placeholders);

    private sealed class PropertyTextState
    {
        public Dictionary<string, SingleTextState> Values { get; } = new(StringComparer.Ordinal);
    }

    private sealed class SingleTextState
    {
        public SingleTextState(string original, string lastApplied) { Original = original; LastApplied = lastApplied; }
        public string Original { get; set; }
        public string LastApplied { get; set; }
    }

    private sealed class AttachedPageState { }

    private sealed class ObservedNodeState
    {
        public ObservedNodeState(bool allowLabelText) => AllowLabelText = allowLabelText;
        public bool AllowLabelText { get; }
        public bool IsApplying { get; set; }
    }
}
