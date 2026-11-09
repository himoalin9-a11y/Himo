using Himo.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Himo.Views;

public sealed class LinkedDevicesPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly VerticalStackLayout _items = new() { Spacing = 10 };
    private readonly Label _status = new() { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
    private readonly string _webUrl;

    public LinkedDevicesPage(HimoApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _webUrl = new Uri(new Uri(_api.BaseUrl), "web/").ToString();
        Title = "الأجهزة المرتبطة";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#0B1020");

        var title = new Label { Text = "الأجهزة المرتبطة", FontSize = 23, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        var hint = new Label
        {
            Text = "يمكنك استخدام حساب Himo على أكثر من هاتف، وربط الكمبيوتر من المتصفح عبر QR. الأجهزة المرتبطة تبقى متصلة حتى تلغي ربطها.",
            FontSize = 13, TextColor = Color.FromArgb("#BBC5DC"), LineBreakMode = LineBreakMode.WordWrap
        };
        var openWeb = new Button { Text = "نسخ رابط Himo Web", HeightRequest = 48, CornerRadius = 14, BackgroundColor = Color.FromArgb("#7650E8"), TextColor = Colors.White };
        openWeb.Clicked += async (_, _) => await Clipboard.Default.SetTextAsync(_webUrl);
        var scan = new Button { Text = "مسح رمز QR لربط الكمبيوتر", HeightRequest = 48, CornerRadius = 14, BackgroundColor = Color.FromArgb("#252B42"), TextColor = Colors.White };
        scan.Clicked += async (_, _) =>
        {
            if (Shell.Current is not null) await Shell.Current.GoToAsync("qr-scanner");
            else await DisplayAlertAsync("مسح QR", "افتح ماسح QR من الصفحة الرئيسية لربط الكمبيوتر.", "حسنًا");
        };
        var copyHint = new Label { Text = "عنوان المتصفح: " + _webUrl, FontSize = 10, TextColor = Color.FromArgb("#8F9BB7"), LineBreakMode = LineBreakMode.CharacterWrap };
        var refresh = new Button { Text = "تحديث قائمة الأجهزة", HeightRequest = 42, CornerRadius = 12, BackgroundColor = Color.FromArgb("#252B42"), TextColor = Colors.White };
        refresh.Clicked += async (_, _) => await LoadDevicesAsync();

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(18, 20), Spacing = 14,
            Children = { title, hint, openWeb, copyHint, scan, refresh, _status, _items }
        };
        Content = new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDevicesAsync();
    }

    private async Task LoadDevicesAsync()
    {
        _status.Text = "جارٍ تحميل الأجهزة...";
        _items.Children.Clear();
        try
        {
            var devices = await _api.GetLinkedDevicesAsync();
            if (devices.Count == 0)
            {
                _status.Text = "لا توجد أجهزة مرتبطة.";
                return;
            }

            foreach (var device in devices)
            {
                var name = new Label { Text = device.DeviceName, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
                var kind = device.DeviceType == "web" ? "متصفح الكمبيوتر" : "تطبيق Himo على الهاتف";
                var details = new Label
                {
                    Text = $"{kind} · تم الربط: {device.CreatedAt.ToLocalTime():yyyy/MM/dd HH:mm} · آخر نشاط: {device.LastSeenAt.ToLocalTime():yyyy/MM/dd HH:mm}",
                    FontSize = 11, TextColor = Color.FromArgb("#AEB9D0")
                };
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10 };
                row.Add(new VerticalStackLayout { Spacing = 4, Children = { name, details } }, 0, 0);
                if (device.IsCurrent)
                {
                    row.Add(new Label { Text = "هذا الجهاز", FontSize = 11, TextColor = Color.FromArgb("#79D7A1"), VerticalTextAlignment = TextAlignment.Center }, 1, 0);
                }
                else
                {
                    var revoke = new Button { Text = "إلغاء الربط", FontSize = 11, Padding = new Thickness(10, 5), CornerRadius = 10, BackgroundColor = Color.FromArgb("#512B3B"), TextColor = Colors.White };
                    revoke.Clicked += async (_, _) =>
                    {
                        var confirm = await DisplayAlertAsync("إلغاء ربط الجهاز", $"هل تريد تسجيل خروج {device.DeviceName}؟", "إلغاء الربط", "إلغاء");
                        if (!confirm) return;
                        try { await _api.RevokeLinkedDeviceAsync(device.DeviceId); await LoadDevicesAsync(); }
                        catch (Exception ex) { await DisplayAlertAsync("تعذر إلغاء الربط", ex.Message, "حسنًا"); }
                    };
                    row.Add(revoke, 1, 0);
                }
                var card = new Border
                {
                    Padding = 13, BackgroundColor = Color.FromArgb("#171E33"), Stroke = Color.FromArgb("#333D58"), StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 15 }, Content = row
                };
                _items.Children.Add(card);
            }
            _status.Text = $"الأجهزة المرتبطة: {devices.Count}";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تحميل الأجهزة: " + ex.Message;
        }
    }
}
