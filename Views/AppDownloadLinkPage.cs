using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls.Shapes;

namespace Himo.Views;

/// <summary>
/// Shows the public Himo installation link configured by the API deployment.
/// The app never fabricates a download URL: an HTTPS URL must be configured
/// on the server before copy/share/open actions become available.
/// </summary>
public sealed class AppDownloadLinkPage : LocalizedContentPage
{
    private readonly HimoApiClient _api;
    private readonly Label _linkLabel;
    private readonly Label _statusLabel;
    private readonly ActivityIndicator _loading;
    private readonly Button _copyButton;
    private readonly Button _shareButton;
    private readonly Button _openButton;
    private readonly Button _retryButton;
    private string? _downloadUrl;
    private int _loadingState;

    public AppDownloadLinkPage(HimoApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        Title = "رابط التطبيق";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        Himo.Services.AppLocalization.AttachToPage(this);

        _linkLabel = new Label
        {
            Text = "",
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.LeftToRight
        };
        _linkLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        _statusLabel = new Label
        {
            Text = "جارٍ تحميل رابط التطبيق...",
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        _statusLabel.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");

        _loading = new ActivityIndicator { IsRunning = true, IsVisible = true, HorizontalOptions = LayoutOptions.Center };
        _loading.SetDynamicResource(ActivityIndicator.ColorProperty, "HimoAccent");

        _copyButton = MakeActionButton("نسخ الرابط", "HimoSoft", "HimoPrimary", CopyClicked);
        _shareButton = MakeActionButton("مشاركة", "HimoSoft", "HimoPrimary", ShareClicked);
        _openButton = MakeActionButton("فتح رابط التنزيل", "HimoAccent", "HimoOnAccent", OpenClicked);
        _retryButton = MakeActionButton("إعادة المحاولة", "HimoSoft", "HimoPrimary", RetryClicked);
        _copyButton.IsEnabled = _shareButton.IsEnabled = _openButton.IsEnabled = false;

        Content = BuildPage();
    }

    private View BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            BackgroundColor = Colors.Transparent
        };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            Padding = new Thickness(18, 14, 18, 12),
            ColumnDefinitions = { new ColumnDefinition(new GridLength(46)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(46)) },
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.RightToLeft
        };
        var back = new Button
        {
            Text = "‹", FontSize = 34, FontAttributes = FontAttributes.Bold,
            WidthRequest = 42, HeightRequest = 42, Padding = 0,
            CornerRadius = 15, BorderWidth = 1,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center
        };
        back.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        back.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        back.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        back.Clicked += BackClicked;
        header.Add(back, 0, 0);

        var title = new Label
        {
            Text = "رابط التطبيق", FontSize = 23, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start, VerticalTextAlignment = TextAlignment.Center
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        header.Add(title, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0);

        var content = new VerticalStackLayout { Padding = new Thickness(16, 10, 16, 28), Spacing = 14 };
        var icon = new Border { WidthRequest = 82, HeightRequest = 82, StrokeThickness = 0, HorizontalOptions = LayoutOptions.Center };
        icon.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        icon.StrokeShape = new RoundRectangle { CornerRadius = 41 };
        icon.Content = new Image
        {
            Source = "himo_pro_ui_settings_icon_globe.png",
            WidthRequest = 42, HeightRequest = 42,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        content.Add(icon);

        var appTitle = new Label
        {
            Text = "Himo", FontSize = 21, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        };
        appTitle.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        content.Add(appTitle);

        var card = new Border { Padding = new Thickness(15), StrokeThickness = 1 };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        card.StrokeShape = new RoundRectangle { CornerRadius = 20 };
        var cardContent = new VerticalStackLayout { Spacing = 12 };
        var linkCaption = new Label { Text = "رابط تنزيل التطبيق", FontSize = 13, FontAttributes = FontAttributes.Bold };
        linkCaption.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var linkBox = new Border { Padding = new Thickness(12), StrokeThickness = 1 };
        linkBox.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        linkBox.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        linkBox.StrokeShape = new RoundRectangle { CornerRadius = 13 };
        linkBox.Content = _linkLabel;
        cardContent.Add(linkCaption);
        cardContent.Add(linkBox);
        card.Content = cardContent;
        content.Add(card);

        content.Add(_loading);
        content.Add(_statusLabel);
        content.Add(_openButton);
        var secondaryActions = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 9 };
        secondaryActions.Add(_copyButton, 0, 0);
        secondaryActions.Add(_shareButton, 1, 0);
        content.Add(secondaryActions);
        content.Add(_retryButton);

        root.Add(header, 0, 0);
        root.Add(new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 0, 1);
        return root;
    }

    private Button MakeActionButton(string text, string backgroundResource, string textResource, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            HeightRequest = 48,
            CornerRadius = 15,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold
        };
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, backgroundResource);
        button.SetDynamicResource(Button.TextColorProperty, textResource);
        button.Clicked += handler;
        return button;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
        if (string.IsNullOrWhiteSpace(_downloadUrl))
            await LoadDownloadUrlAsync();
    }

    private async Task LoadDownloadUrlAsync()
    {
        if (Interlocked.Exchange(ref _loadingState, 1) != 0) return;
        _loading.IsVisible = true;
        _loading.IsRunning = true;
        _retryButton.IsVisible = false;
        _statusLabel.Text = "جارٍ تحميل رابط التطبيق...";
        try
        {
            _downloadUrl = await _api.GetAppDownloadUrlAsync();
            var available = !string.IsNullOrWhiteSpace(_downloadUrl);
            _linkLabel.Text = available ? _downloadUrl : "لم يتم نشر رابط تنزيل التطبيق بعد.";
            _copyButton.IsEnabled = _shareButton.IsEnabled = _openButton.IsEnabled = available;
            _statusLabel.Text = available
                ? "يمكنك نسخ الرابط أو مشاركته أو فتحه لتنزيل Himo."
                : "سيظهر الرابط هنا بعد ضبط رابط APK أو صفحة المتجر في إعدادات خادم Himo.";
        }
        catch (Exception ex)
        {
            _downloadUrl = null;
            _linkLabel.Text = "";
            _copyButton.IsEnabled = _shareButton.IsEnabled = _openButton.IsEnabled = false;
            _statusLabel.Text = "تعذر تحميل رابط التطبيق. تحقق من اتصال الإنترنت ثم أعد المحاولة.";
            System.Diagnostics.Debug.WriteLine($"[Himo AppLink] {ex}");
            _retryButton.IsVisible = true;
        }
        finally
        {
            _loading.IsRunning = false;
            _loading.IsVisible = false;
            Interlocked.Exchange(ref _loadingState, 0);
        }
    }

    private async void CopyClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_downloadUrl)) return;
        try
        {
            await Clipboard.Default.SetTextAsync(_downloadUrl);
            await DisplayAlertAsync("رابط التطبيق", "تم نسخ رابط تنزيل Himo.", "حسنًا");
        }
        catch (Exception ex) { await DisplayAlertAsync("رابط التطبيق", "تعذر نسخ الرابط: " + ex.Message, "حسنًا"); }
    }

    private async void ShareClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_downloadUrl)) return;
        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "تنزيل تطبيق Himo",
                Text = "حمّل تطبيق Himo من الرابط التالي:\n" + _downloadUrl
            });
        }
        catch (Exception ex) { await DisplayAlertAsync("مشاركة التطبيق", "تعذرت مشاركة الرابط: " + ex.Message, "حسنًا"); }
    }

    private async void OpenClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_downloadUrl)) return;
        try { await Launcher.Default.OpenAsync(new Uri(_downloadUrl)); }
        catch (Exception ex) { await DisplayAlertAsync("تنزيل Himo", "تعذر فتح الرابط: " + ex.Message, "حسنًا"); }
    }

    private async void RetryClicked(object? sender, EventArgs e) => await LoadDownloadUrlAsync();

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..");
    }
}
