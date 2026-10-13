using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;

namespace Himo.Views;

/// <summary>A country calling-code entry used by the Himo phone-number editor.</summary>
public sealed record CountryDialCode(string Name, string Flag, string DialCode)
{
    public string CompactDisplay => $"{Flag}  {DialCode}  ▾";
    public string SearchText => $"{Name} {DialCode} {Flag}";
}

/// <summary>Searchable, touch-friendly country selector with RTL Arabic presentation.</summary>
public sealed class CountryDialCodePickerPage : ContentPage
{
    private readonly IReadOnlyList<CountryDialCode> _countries;
    private readonly ObservableCollection<CountryDialCode> _visibleCountries = new();
    private readonly TaskCompletionSource<CountryDialCode?> _selection =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CollectionView _countryList;
    private readonly SearchBar _searchBar;

    public Task<CountryDialCode?> SelectionTask => _selection.Task;

    public CountryDialCodePickerPage(IReadOnlyList<CountryDialCode> countries, CountryDialCode selectedCountry)
    {
        _countries = countries;
        Title = "اختيار الدولة";
        BackgroundColor = Color.FromArgb("#0B142B");
        FlowDirection = FlowDirection.RightToLeft;
        Padding = new Thickness(0);

        foreach (var country in countries)
            _visibleCountries.Add(country);

        var title = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };
        title.Children.Add(new Label
        {
            Text = "رمز الدولة",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.End
        });
        title.Children.Add(new Label
        {
            Text = "ابحث باسم الدولة أو رمز الاتصال الدولي",
            FontSize = 12,
            TextColor = Color.FromArgb("#A6B9E9"),
            HorizontalTextAlignment = TextAlignment.End
        });

        var closeButton = new Button
        {
            Text = "إلغاء",
            FontSize = 13,
            TextColor = Color.FromArgb("#DDE5FF"),
            BackgroundColor = Color.FromArgb("#1B2850"),
            BorderColor = Color.FromArgb("#334A82"),
            BorderWidth = 1,
            CornerRadius = 13,
            Padding = new Thickness(11, 0),
            HeightRequest = 42,
            VerticalOptions = LayoutOptions.Center
        };
        closeButton.Clicked += (_, _) => _selection.TrySetResult(null);

        var header = new Grid
        {
            Padding = new Thickness(16, 18, 16, 8),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Star)
            },
            ColumnSpacing = 12,
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
            TextColor = Colors.White,
            PlaceholderColor = Color.FromArgb("#8496C4"),
            BackgroundColor = Color.FromArgb("#101D3D"),
            CancelButtonColor = Color.FromArgb("#BFCBFF"),
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0)
        };
        _searchBar.TextChanged += SearchTextChanged;
        var searchBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#101D3D"),
            Stroke = Color.FromArgb("#2D4274"),
            StrokeThickness = 1,
            Padding = new Thickness(4, 0),
            Content = _searchBar,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            Margin = new Thickness(16, 3, 16, 8)
        };

        _countryList = new CollectionView
        {
            ItemsSource = _visibleCountries,
            SelectionMode = SelectionMode.Single,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 7 },
            ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Margin = new Thickness(16, 0),
            EmptyView = new Label
            {
                Text = "لا توجد نتائج. جرّب اسمًا أو رمزًا آخر.",
                TextColor = Color.FromArgb("#A6B9E9"),
                FontSize = 13,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(12, 22)
            },
            ItemTemplate = new DataTemplate(CreateCountryTemplate)
        };
        _countryList.SelectionChanged += CountrySelected;

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)
            },
            RowSpacing = 6,
            BackgroundColor = Color.FromArgb("#0B142B")
        };
        layout.Children.Add(header);
        Grid.SetRow(header, 0);
        layout.Children.Add(searchBorder);
        Grid.SetRow(searchBorder, 1);
        layout.Children.Add(_countryList);
        Grid.SetRow(_countryList, 2);
        Content = layout;

        // Keep current choice discoverable near the top by default.
        var currentIndex = _visibleCountries.IndexOf(selectedCountry);
        if (currentIndex > 0)
        {
            _visibleCountries.RemoveAt(currentIndex);
            _visibleCountries.Insert(0, selectedCountry);
        }
    }

    private static View CreateCountryTemplate()
    {
        var flag = new Label
        {
            FontSize = 25,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };
        flag.SetBinding(Label.TextProperty, nameof(CountryDialCode.Flag));

        var countryName = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#F1F4FF"),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        countryName.SetBinding(Label.TextProperty, nameof(CountryDialCode.Name));

        var code = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#BAC9FF"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        code.SetBinding(Label.TextProperty, nameof(CountryDialCode.DialCode));

        var grid = new Grid
        {
            Padding = new Thickness(12, 8),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)
            },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.RightToLeft,
            MinimumHeightRequest = 56
        };
        grid.Children.Add(flag);
        Grid.SetColumn(flag, 0);
        grid.Children.Add(countryName);
        Grid.SetColumn(countryName, 1);
        grid.Children.Add(code);
        Grid.SetColumn(code, 2);

        return new Border
        {
            Content = grid,
            BackgroundColor = Color.FromArgb("#101D3D"),
            Stroke = Color.FromArgb("#263B6B"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            Margin = new Thickness(0, 1)
        };
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = (e.NewTextValue ?? string.Empty).Trim();
        _visibleCountries.Clear();
        foreach (var country in _countries.Where(country =>
                     string.IsNullOrWhiteSpace(query)
                     || country.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)))
            _visibleCountries.Add(country);
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
