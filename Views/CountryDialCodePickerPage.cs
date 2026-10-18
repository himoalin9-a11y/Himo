using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Himo.Views;

/// <summary>A country calling-code entry used by the Himo phone-number editor.</summary>
public sealed record CountryDialCode(string Name, string Flag, string DialCode)
{
    public string CompactDisplay => $"{Flag}  {DialCode}  ▾";
    public string SearchText => $"{Name} {DialCode} {Flag}";
}

/// <summary>Theme-aware country selector with a compact, RTL Arabic layout.</summary>
public sealed class CountryDialCodePickerPage : ContentPage
{
    private readonly IReadOnlyList<CountryDialCode> _countries;
    private readonly ObservableCollection<CountryDialCode> _visibleCountries = new();
    private readonly TaskCompletionSource<CountryDialCode?> _selection =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CollectionView _countryList;
    private readonly SearchBar _searchBar;
    private readonly string _selectedDialCode;

    public Task<CountryDialCode?> SelectionTask => _selection.Task;

    public CountryDialCodePickerPage(IReadOnlyList<CountryDialCode> countries, CountryDialCode selectedCountry)
    {
        _countries = countries ?? Array.Empty<CountryDialCode>();
        _selectedDialCode = selectedCountry?.DialCode ?? string.Empty;
        Title = "اختيار الدولة";
        FlowDirection = FlowDirection.RightToLeft;
        Padding = 0;
        SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        foreach (var country in _countries)
            _visibleCountries.Add(country);

        var title = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var heading = new Label
        {
            Text = "رمز الدولة",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        heading.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var subtitle = new Label
        {
            Text = "ابحث باسم الدولة أو رمز الاتصال الدولي",
            FontSize = 12.5,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.WordWrap
        };
        subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        title.Children.Add(heading);
        title.Children.Add(subtitle);

        var closeButton = new ImageButton
        {
            Source = "himo_pro_ui_himo_ui_close.png",
            WidthRequest = 42,
            HeightRequest = 42,
            Padding = 11,
            CornerRadius = 21,
            BorderWidth = 1,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        closeButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        closeButton.SetDynamicResource(ImageButton.BorderColorProperty, "HimoBorder");
        SemanticProperties.SetDescription(closeButton, "إغلاق اختيار الدولة");
        closeButton.Clicked += (_, _) => _selection.TrySetResult(null);

        var header = new Grid
        {
            Padding = new Thickness(18, 18, 18, 12),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Star)
            },
            ColumnSpacing = 13,
            FlowDirection = FlowDirection.RightToLeft
        };
        header.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 0);
        header.Children.Add(title);
        Grid.SetColumn(title, 1);

        _searchBar = new SearchBar
        {
            Placeholder = "مثال: السعودية أو +966",
            FontSize = 14,
            BackgroundColor = Colors.Transparent,
            CancelButtonColor = Colors.Gray,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = 0
        };
        _searchBar.SetDynamicResource(SearchBar.TextColorProperty, "HimoPrimary");
        _searchBar.SetDynamicResource(SearchBar.PlaceholderColorProperty, "HimoTextSecondary");
        _searchBar.SetDynamicResource(SearchBar.CancelButtonColorProperty, "HimoAccent");
        _searchBar.TextChanged += SearchTextChanged;

        var searchIcon = new Image
        {
            Source = "himo_pro_ui_icon_search.png",
            WidthRequest = 21,
            HeightRequest = 21,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center
        };
        var searchContent = new Grid
        {
            Padding = new Thickness(12, 1),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Star)
            },
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.RightToLeft
        };
        searchContent.Children.Add(searchIcon);
        Grid.SetColumn(searchIcon, 0);
        searchContent.Children.Add(_searchBar);
        Grid.SetColumn(_searchBar, 1);

        var searchBorder = new Border
        {
            Padding = 0,
            Content = searchContent,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Margin = new Thickness(18, 0, 18, 10)
        };
        searchBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoInput");
        searchBorder.SetDynamicResource(Border.StrokeProperty, "HimoBorder");

        var emptyIcon = new Image
        {
            Source = "himo_pro_ui_icon_search.png",
            WidthRequest = 32,
            HeightRequest = 32,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center
        };
        var emptyTitle = new Label
        {
            Text = "لا توجد نتائج",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        };
        emptyTitle.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var emptyHint = new Label
        {
            Text = "جرّب اسم دولة آخر أو رمز الاتصال الدولي.",
            FontSize = 12.5,
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        emptyHint.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        var emptyView = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(28, 26),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            Children = { emptyIcon, emptyTitle, emptyHint }
        };

        _countryList = new CollectionView
        {
            ItemsSource = _visibleCountries,
            SelectionMode = SelectionMode.Single,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 },
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Margin = new Thickness(18, 0),
            EmptyView = emptyView,
            ItemTemplate = new DataTemplate(CreateCountryTemplate)
        };
        _countryList.SelectionChanged += CountrySelected;

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)
            },
            RowSpacing = 4
        };
        layout.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        layout.Children.Add(header);
        Grid.SetRow(header, 0);
        layout.Children.Add(searchBorder);
        Grid.SetRow(searchBorder, 1);
        layout.Children.Add(_countryList);
        Grid.SetRow(_countryList, 2);
        Content = layout;

        // Keep the selected country at the top while retaining the full searchable list.
        var currentIndex = -1;
        for (var i = 0; i < _visibleCountries.Count; i++)
        {
            if (string.Equals(_visibleCountries[i].DialCode, _selectedDialCode, StringComparison.Ordinal))
            {
                currentIndex = i;
                break;
            }
        }
        if (currentIndex > 0)
        {
            var current = _visibleCountries[currentIndex];
            _visibleCountries.RemoveAt(currentIndex);
            _visibleCountries.Insert(0, current);
        }
    }

    private View CreateCountryTemplate()
    {
        var flag = new Label
        {
            FontSize = 25,
            WidthRequest = 38,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };
        flag.SetBinding(Label.TextProperty, nameof(CountryDialCode.Flag));

        var countryName = new Label
        {
            FontSize = 14.5,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };
        countryName.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        countryName.SetBinding(Label.TextProperty, nameof(CountryDialCode.Name));

        var code = new Label
        {
            FontSize = 13.5,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumWidthRequest = 54
        };
        code.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        code.SetBinding(Label.TextProperty, nameof(CountryDialCode.DialCode));

        var selectedCheck = new Image
        {
            Source = "himo_pro_ui_settings_icon_check.png",
            WidthRequest = 17,
            HeightRequest = 17,
            Aspect = Aspect.AspectFit,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Center
        };

        var grid = new Grid
        {
            Padding = new Thickness(12, 10),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)
            },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.RightToLeft,
            MinimumHeightRequest = 62
        };
        grid.Children.Add(flag);
        Grid.SetColumn(flag, 0);
        grid.Children.Add(countryName);
        Grid.SetColumn(countryName, 1);
        grid.Children.Add(code);
        Grid.SetColumn(code, 2);
        grid.Children.Add(selectedCheck);
        Grid.SetColumn(selectedCheck, 3);

        var card = new Border
        {
            Content = grid,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Margin = new Thickness(0, 1)
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        card.BindingContextChanged += (_, _) =>
        {
            var isSelected = card.BindingContext is CountryDialCode item &&
                string.Equals(item.DialCode, _selectedDialCode, StringComparison.Ordinal);
            selectedCheck.IsVisible = isSelected;
            card.StrokeThickness = isSelected ? 1.6 : 1;
            card.SetDynamicResource(VisualElement.BackgroundColorProperty, isSelected ? "HimoSoft" : "HimoCard");
            card.SetDynamicResource(Border.StrokeProperty, isSelected ? "HimoAccent" : "HimoBorder");
        };
        return card;
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = (e.NewTextValue ?? string.Empty).Trim();
        _visibleCountries.Clear();
        foreach (var country in _countries.Where(country =>
                     string.IsNullOrWhiteSpace(query) ||
                     country.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            _visibleCountries.Add(country);
        }
    }

    private void CountrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CountryDialCode country)
            _selection.TrySetResult(country);
    }

    protected override bool OnBackButtonPressed()
    {
        _selection.TrySetResult(null);
        return true; // The owning SettingsPage closes the modal after the result arrives.
    }
}
