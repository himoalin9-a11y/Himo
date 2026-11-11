using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Himo.Views;

/// <summary>Unauthenticated companion-phone QR flow, launched from the login overflow menu.</summary>
public sealed class MobileCompanionPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly CameraBarcodeReaderView _scanner;
    private readonly Label _status;
    private int _handling;
    private bool _active;
    private bool _cameraReady;

    public MobileCompanionPage(HimoApiClient api, AccountService account)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        Title = AppLocalization.TranslateSourceText("إضافة كهاتف مساعد");
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#080B12");
        AppLocalization.AttachToPage(this);

        _scanner = new CameraBarcodeReaderView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormats.TwoDimensional,
                AutoRotate = true,
                Multiple = false,
                TryHarder = true,
                InitialDelayBeforeAnalyzingFrames = 250,
                DelayBetweenAnalyzingFrames = 150,
                DelayBetweenContinuousScans = 900
            },
            IsDetecting = false,
            IsEnabled = false,
            IsVisible = false
        };
        _scanner.BarcodesDetected += OnBarcodesDetected;
        _status = new Label
        {
            Text = AppLocalization.TranslateSourceText("افتح الأجهزة المرتبطة على هاتفك الأساسي، ثم اعرض رمز ربط الهاتف المساعد"),
            FontSize = 13,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };

        var back = new Button
        {
            Text = AppLocalization.TranslateSourceText("رجوع"),
            HeightRequest = 44,
            CornerRadius = 13,
            BackgroundColor = Color.FromArgb("#252B42"),
            TextColor = Colors.White
        };
        back.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var title = new Label
        {
            Text = AppLocalization.TranslateSourceText("إضافة كهاتف مساعد"),
            FontSize = 21,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center
        };
        var guide = new Label
        {
            Text = AppLocalization.TranslateSourceText("وجّه الكاميرا إلى رمز QR المعروض على الهاتف الأساسي. لن تحتاج إلى إدخال البريد أو كلمة المرور."),
            FontSize = 13,
            TextColor = Color.FromArgb("#B8C3DA"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        var overlay = new VerticalStackLayout
        {
            Padding = new Thickness(20, 20),
            Spacing = 14,
            Children = { back, title, guide, _status }
        };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        root.Add(_scanner, 0, 0);
        var bottom = new Border
        {
            Padding = new Thickness(18, 16),
            BackgroundColor = Color.FromArgb("#E8080B12"),
            Stroke = Color.FromArgb("#334A6A"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(22, 22, 0, 0) },
            Content = overlay
        };
        root.Add(bottom, 0, 1);
        Content = root;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _active = true;
        _cameraReady = false;
        Interlocked.Exchange(ref _handling, 0);
        _ = InitializeCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _active = false;
        _cameraReady = false;
        _scanner.IsDetecting = false;
        _scanner.IsEnabled = false;
        _scanner.IsVisible = false;
        base.OnDisappearing();
    }

    private async Task InitializeCameraAsync()
    {
#if ANDROID
        var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
            permission = await Permissions.RequestAsync<Permissions.Camera>();
        if (!_active) return;
        if (permission != PermissionStatus.Granted)
        {
            _status.Text = AppLocalization.TranslateSourceText("يلزم السماح باستخدام الكاميرا لمسح رمز الهاتف الأساسي.");
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("إذن الكاميرا"), AppLocalization.TranslateSourceText("فعّل إذن الكاميرا لـ Himo ثم حاول مجددًا."), AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }
#endif
        if (!_active) return;
        if (!BarcodeScanning.IsSupported)
        {
            _status.Text = AppLocalization.TranslateSourceText("مسح QR غير مدعوم على هذا الجهاز.");
            return;
        }
        _cameraReady = true;
        _scanner.IsEnabled = true;
        _scanner.IsVisible = true;
        _scanner.IsDetecting = true;
        _status.Text = AppLocalization.TranslateSourceText("امسح رمز QR من الهاتف الأساسي.");
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (!_active || !_cameraReady || Volatile.Read(ref _handling) != 0) return;
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value) || Interlocked.Exchange(ref _handling, 1) != 0) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_active) { Interlocked.Exchange(ref _handling, 0); return; }
            _scanner.IsDetecting = false;
            try { await ProcessPairingQrAsync(value); }
            catch (Exception ex)
            {
                _status.Text = AppLocalization.TranslateSourceText("تعذر ربط الهاتف. حاول مسح الرمز مرة أخرى.");
                await DisplayAlertAsync(AppLocalization.TranslateSourceText("تعذر ربط الهاتف"), Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
            }
            finally
            {
                if (_active && _cameraReady)
                {
                    Interlocked.Exchange(ref _handling, 0);
                    _scanner.IsDetecting = true;
                }
            }
        });
    }

    private async Task ProcessPairingQrAsync(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(AppLocalization.TranslateSourceText("رمز QR غير صالح."));
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || !segments[0].Equals("link-mobile", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(segments[1], out var pairingId) || pairingId == Guid.Empty)
            throw new InvalidOperationException(AppLocalization.TranslateSourceText("هذا الرمز ليس رمز ربط هاتف مساعد من Himo."));
        var qrKey = ReadQuery(uri, "key");
        if (!IsSecret(qrKey))
            throw new InvalidOperationException(AppLocalization.TranslateSourceText("بيانات رمز الربط غير مكتملة."));

        await _api.TokenInitialization;
        _status.Text = AppLocalization.TranslateSourceText("تمت قراءة الرمز. جارٍ إرسال طلب الربط للهاتف الأساسي...");
        _scanner.IsDetecting = false;
        var pollSecret = await _api.ClaimMobilePairingAsync(pairingId, qrKey!);
        _status.Text = AppLocalization.TranslateSourceText("تم إرسال الطلب. وافق على هذا الهاتف من الهاتف الأساسي لإكمال الربط.");

        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (_active && DateTimeOffset.UtcNow < deadline)
        {
            var result = await _api.PollMobilePairingAsync(pairingId, pollSecret!);
            if (string.Equals(result.Status, "approved", StringComparison.OrdinalIgnoreCase))
            {
                var linkedAccount = _api.AcceptMobilePairingSession(result);
                _account.SignIn(linkedAccount.UserId, linkedAccount.Email, linkedAccount.Name);
                try { await _api.CompleteMobilePairingAsync(pairingId, pollSecret); }
                catch (Exception cleanupError) { System.Diagnostics.Debug.WriteLine("[Himo Mobile Pair] Completion cleanup: " + cleanupError); }
#if ANDROID
                _ = AppRuntimeBridge.RegisterCurrentPushTokenAsync();
#endif
                AppRuntimeBridge.MarkSessionUnlocked();
                _status.Text = AppLocalization.TranslateSourceText("تم ربط الهاتف بنجاح. جارٍ فتح Himo...");
                var window = Application.Current?.Windows.FirstOrDefault();
                if (window is not null) window.Page = new AppShell();
                return;
            }
            if (string.Equals(result.Status, "expired", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(AppLocalization.TranslateSourceText("انتهت صلاحية الرمز. أنشئ رمزًا جديدًا من الهاتف الأساسي."));
            if (string.Equals(result.Status, "denied", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(AppLocalization.TranslateSourceText("رفض الهاتف الأساسي طلب الربط."));
            await Task.Delay(1800);
        }
        if (_active) throw new TimeoutException(AppLocalization.TranslateSourceText("لم تصل موافقة الهاتف الأساسي خلال دقيقتين. أنشئ رمزًا جديدًا وحاول مرة أخرى."));
    }

    private static string? ReadQuery(Uri uri, string key)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && Uri.UnescapeDataString(pair[0]).Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[1]);
        }
        return null;
    }

    private static bool IsSecret(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
