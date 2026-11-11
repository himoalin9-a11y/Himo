using Himo.Services;
using Microsoft.Maui.Controls.Shapes;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Himo.Views;

/// <summary>Shows a short-lived QR on the authenticated primary phone and asks for explicit approval.</summary>
public sealed class MobilePairingQrPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly Label _status;
    private readonly BarcodeGeneratorView _qr;
    private CancellationTokenSource? _lifetime;
    private MobilePairingQrInfo? _pairing;
    private bool _completed;
    private int _approvalPrompt;

    public MobilePairingQrPage(HimoApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        Title = AppLocalization.TranslateSourceText("ربط هاتف مساعد");
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#0B1020");
        AppLocalization.AttachToPage(this);

        var title = new Label { Text = AppLocalization.TranslateSourceText("ربط هاتف مساعد"), FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center };
        var hint = new Label
        {
            Text = AppLocalization.TranslateSourceText("افتح صفحة تسجيل الدخول على الهاتف الآخر، واضغط ⋮ ثم «إضافة كهاتف مساعد» وامسح هذا الرمز. لا تشارك الرمز مع أي شخص آخر."),
            FontSize = 13, TextColor = Color.FromArgb("#B9C5DF"), HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap
        };
        _qr = new BarcodeGeneratorView
        {
            Format = BarcodeFormat.QrCode,
            Value = string.Empty,
            WidthRequest = 260,
            HeightRequest = 260,
            BackgroundColor = Colors.White,
            ForegroundColor = Color.FromArgb("#160A2C"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        var qrFrame = new Border
        {
            WidthRequest = 286, HeightRequest = 286, Padding = 12,
            BackgroundColor = Colors.White, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            HorizontalOptions = LayoutOptions.Center,
            Content = _qr
        };
        _status = new Label { Text = AppLocalization.TranslateSourceText("جارٍ إنشاء رمز آمن..."), FontSize = 13, TextColor = Color.FromArgb("#D7DDF0"), HorizontalTextAlignment = TextAlignment.Center };
        var cancel = new Button { Text = AppLocalization.TranslateSourceText("إغلاق وإلغاء الرمز"), HeightRequest = 48, CornerRadius = 14, BackgroundColor = Color.FromArgb("#38263A"), TextColor = Colors.White };
        cancel.Clicked += async (_, _) => await Navigation.PopAsync();
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(22, 28), Spacing = 20,
                Children = { title, hint, qrFrame, _status, cancel }
            },
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _ = StartAndWaitAsync(_lifetime.Token);
    }

    protected override void OnDisappearing()
    {
        _lifetime?.Cancel();
        if (!_completed && _pairing is not null)
            _ = _api.CancelMobilePairingAsync(_pairing.PairingId);
        base.OnDisappearing();
    }

    private async Task StartAndWaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _api.TokenInitialization;
            _pairing = await _api.StartMobilePairingAsync(cancellationToken);
            _qr.Value = _pairing.QrText;
            _qr.IsVisible = true;
            _status.Text = AppLocalization.TranslateSourceText("امسح الرمز الآن. تنتهي صلاحيته خلال دقيقتين.");

            while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < _pairing.ExpiresAt)
            {
                var pending = await _api.GetPendingMobilePairingsAsync(cancellationToken);
                var request = pending.FirstOrDefault(item => item.PairingId == _pairing.PairingId);
                if (request is not null && Interlocked.Exchange(ref _approvalPrompt, 1) == 0)
                {
                    _status.Text = AppLocalization.TranslateSourceText($"طلب ربط من: {request.DeviceName}. بانتظار قرارك...");
                    var approve = await MainThread.InvokeOnMainThreadAsync(() => DisplayAlertAsync(
                        AppLocalization.TranslateSourceText("تأكيد هاتف مساعد"),
                        AppLocalization.TranslateSourceText($"هل تسمح بربط {request.DeviceName} بحسابك؟ سيتمكن الهاتف من الوصول إلى محادثاتك وإرسال الرسائل، ويمكنك إلغاء ربطه لاحقًا من الأجهزة المرتبطة."),
                        AppLocalization.TranslateSourceText("موافقة وربط"), AppLocalization.TranslateSourceText("رفض")));
                    if (approve)
                    {
                        await _api.ApproveMobilePairingAsync(request.PairingId, cancellationToken);
                        _completed = true;
                        _status.Text = AppLocalization.TranslateSourceText($"تم ربط {request.DeviceName} بنجاح. يمكنك الآن استخدام الحساب على الهاتفين.");
                        _qr.IsVisible = false;
                        return;
                    }
                    await _api.CancelMobilePairingAsync(request.PairingId, cancellationToken);
                    _status.Text = AppLocalization.TranslateSourceText("تم رفض طلب الربط. يمكنك إنشاء رمز جديد عند الحاجة.");
                    _qr.IsVisible = false;
                    return;
                }
                await Task.Delay(1600, cancellationToken);
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                _status.Text = AppLocalization.TranslateSourceText("انتهت صلاحية الرمز. أغلق الصفحة وافتحها لإنشاء رمز جديد.");
                _qr.IsVisible = false;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                _status.Text = AppLocalization.TranslateSourceText("تعذر إنشاء الربط: ") + ex.Message;
        }
    }
}
