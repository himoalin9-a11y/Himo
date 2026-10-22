using System.Collections;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    private static readonly string[] SupportedCodes = { "ar", "en", "fr", "tr", "ur", "es", "id" };
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("ar");

    public static string CurrentLanguageCode => _culture.TwoLetterISOLanguageName;
    public static bool IsRightToLeft => CurrentLanguageCode is "ar" or "ur";
    public static FlowDirection PreferredFlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Loads the saved culture before the first application page is shown.</summary>
    public static void InitializeFromPreferences()
    {
        var stored = Preferences.Default.Get(PreferenceKey, "ar");
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
            var app = Application.Current;
            if (app is null) return;

            foreach (var window in app.Windows)
            {
                if (window.Page is not Page page) continue;
                page.FlowDirection = PreferredFlowDirection;
                if (page is Shell shell)
                    ApplyToShell(shell);
                ApplyToPage(page);
            }

            if (Shell.Current is { } currentShell)
            {
                ApplyToShell(currentShell);
                if (currentShell.CurrentPage is { } currentPage)
                    ApplyToPage(currentPage);
            }
        });
    }

    public static string T(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        return ResourceManager.GetString(key, _culture) ?? key;
    }

    /// <summary>Translates a fixed source-language phrase when a resource exists; otherwise returns it unchanged.</summary>
    public static string TranslateSourceText(string? sourceText)
        => string.IsNullOrEmpty(sourceText) ? sourceText ?? string.Empty : TranslateText(sourceText);

    public static void AttachToPage(Page page)
    {
        if (page is null) return;
        if (!AttachedPages.TryGetValue(page, out _))
        {
            AttachedPages.Add(page, new AttachedPageState());
            page.Appearing += (_, _) => ApplyToPage(page);
        }
        ApplyToPage(page);
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
                // Do not rewrite labels that display message/contact/list content; the
                // surrounding item template can contain arbitrary user-provided text.
                if (allowLabelText)
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
            textState = new SingleTextState(currentValue, currentValue);
            state.Values[propertyName] = textState;
        }
        else if (!string.Equals(currentValue, textState.LastApplied, StringComparison.Ordinal))
        {
            // A control was updated by the page's own logic. Treat that new value
            // as the new source phrase instead of restoring stale text later.
            textState.Original = currentValue;
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

        if (SourceTextToKey.Value.TryGetValue(lookupText, out var key))
        {
            var translated = ResourceManager.GetString(key, _culture) ?? lookupText;
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

            map.TryAdd(value.Trim(), key);
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
            _culture = CultureInfo.GetCultureInfo("ar");
        }

        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
    }

    private static string NormalizeCultureCode(string? code)
    {
        var normalized = (code ?? "ar").Trim().ToLowerInvariant();
        return SupportedCodes.Contains(normalized, StringComparer.Ordinal) ? normalized : "ar";
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
