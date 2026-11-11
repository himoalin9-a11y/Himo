using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.ApplicationModel;
using ZXing.Net.Maui;

namespace Himo.Views;

public partial class QrScannerPage : ContentPage, IQueryAttributable
{
    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private readonly AccountService _account;
    private int _handlingScan;
    private string? _incomingProfileLink;
    private bool _cameraReady;
    private bool _pageActive;

    public QrScannerPage(HimoApiClient api, HomeViewModel home, AccountService account)
    {
        InitializeComponent();
        _api = api;
        _home = home;
        _account = account;
        ScannerView.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.TwoDimensional,
            AutoRotate = true,
            Multiple = false,
            TryHarder = true,
            InitialDelayBeforeAnalyzingFrames = 250,
            DelayBetweenAnalyzingFrames = 150,
            DelayBetweenContinuousScans = 900
        };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("value", out var value) && value is not null)
            _incomingProfileLink = value.ToString();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _pageActive = true;
        Interlocked.Exchange(ref _handlingScan, 0);
        _cameraReady = false;
        AppLocalization.AttachToPage(this);
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(this);
        _ = InitializeScannerAsync();
    }

    protected override void OnDisappearing()
    {
        _pageActive = false;
        _cameraReady = false;
        ScannerView.IsDetecting = false;
        ScannerView.IsEnabled = false;
        ScannerView.IsVisible = false;
        base.OnDisappearing();
    }

    private async Task InitializeScannerAsync()
    {
        ScannerView.IsDetecting = false;
        ScannerView.IsEnabled = false;
        ScannerView.IsVisible = false;
        if (!string.IsNullOrWhiteSpace(_incomingProfileLink))
        {
            var incomingLink = _incomingProfileLink;
            _incomingProfileLink = null;
            await HandleHimoLinkAsync(incomingLink);
            // If the user declines the shared link, continue into the scanner.
            // If the link opened a chat, OnDisappearing flips _pageActive to false.
            if (!_pageActive) return;
        }

#if ANDROID
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();
            if (!_pageActive) return;
            if (status != PermissionStatus.Granted)
            {
                ScannerStatusLabel.Text = "يلزم السماح باستخدام الكاميرا لمسح رمز QR.";
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("إذن الكاميرا"),Himo.Services.AppLocalization.TranslateSourceText("فعّل إذن الكاميرا لـ Himo ثم حاول مجددًا."),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }
        }
        catch (Exception ex)
        {
            ScannerStatusLabel.Text = "تعذر تشغيل الكاميرا.";
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الكاميرا"), Himo.Services.AppLocalization.TranslateSourceText(ex.Message),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }
#endif
        if (!_pageActive) return;
        if (ZXing.Net.Maui.BarcodeScanning.IsSupported)
        {
            _cameraReady = true;
            ScannerView.IsEnabled = true;
            ScannerView.IsVisible = true;
            ScannerView.IsDetecting = true;
            ScannerStatusLabel.Text = "امسح رمز حساب أو مجموعة Himo.";
        }
        else
        {
            ScannerView.IsDetecting = false;
            ScannerStatusLabel.Text = "مسح QR بالكاميرا غير مدعوم على هذا الجهاز.";
        }
    }

    private void BarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        // Camera callbacks may arrive one frame after the page was covered or popped.
        // Ignore those callbacks so they cannot show dialogs or navigate from a stale page.
        if (!_pageActive || !_cameraReady || Volatile.Read(ref _handlingScan) != 0) return;
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;
        if (Interlocked.Exchange(ref _handlingScan, 1) != 0) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_pageActive)
            {
                Interlocked.Exchange(ref _handlingScan, 0);
                return;
            }
            try
            {
                ScannerView.IsDetecting = false;
                await HandleHimoLinkAsync(value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo QR] Scan handling failed: {ex}");
                try { await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تعذر قراءة الرمز"),Himo.Services.AppLocalization.TranslateSourceText("لم نتمكن من معالجة هذا الرمز. وجّه الكاميرا إلى رمز Himo وحاول مجددًا."),Himo.Services.AppLocalization.TranslateSourceText("حسنًا")); }
                catch { }
            }
            finally
            {
                // Always release the scan gate after a failed or cancelled result.
                // This also makes the camera reusable when the user returns from a chat.
                if (_pageActive && _cameraReady)
                {
                    Interlocked.Exchange(ref _handlingScan, 0);
                    ScannerView.IsDetecting = true;
                }
            }
        });
    }

    private async Task HandleHimoLinkAsync(string value)
    {
        if (!HimoAppLinkRouter.TryNormalizeLink(value, out var uri, _api.BaseUrl))
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("رمز غير صالح"),Himo.Services.AppLocalization.TranslateSourceText("هذا الرمز لا يحتوي على رابط Himo صالح."),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            ScannerStatusLabel.Text = "امسح رمز حساب أو مجموعة Himo.";
            return;
        }

        try { await _api.TokenInitialization; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo QR] Token initialization failed: {ex.Message}"); }

        if (!_api.HasToken)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تسجيل الدخول"),Himo.Services.AppLocalization.TranslateSourceText("سجّل الدخول إلى Himo أولًا."),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        try
        {
            if (uri.Host.Equals("link-device", StringComparison.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/'), out var pairingId) || pairingId == Guid.Empty)
                    throw new InvalidOperationException("معرّف ربط الكمبيوتر غير صالح.");
                var qrKey = HimoAppLinkRouter.ReadQueryValue(uri, "key");
                var deviceName = HimoAppLinkRouter.ReadQueryValue(uri, "device") ?? "متصفح الكمبيوتر";
                if (string.IsNullOrWhiteSpace(qrKey) || qrKey.Length != 64 || qrKey.Any(ch => !Uri.IsHexDigit(ch)))
                    throw new InvalidOperationException("رمز ربط الكمبيوتر غير صالح.");

                var approved = await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تأكيد ربط جهاز جديد"),Himo.Services.AppLocalization.TranslateSourceText($"هل تريد ربط {deviceName} بحساب Himo؟ سيصبح بإمكان هذا المتصفح قراءة محادثاتك وإرسال الرسائل حتى تلغي ربطه."),Himo.Services.AppLocalization.TranslateSourceText("ربط الجهاز"),Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
                if (!approved) return;

                await _api.ApproveWebPairingAsync(pairingId, qrKey, deviceName);
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تم ربط الكمبيوتر"),Himo.Services.AppLocalization.TranslateSourceText($"تم ربط {deviceName} بنجاح. يمكنك الآن استخدام Himo من المتصفح."),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                ScannerStatusLabel.Text = "تم ربط جهاز الكمبيوتر بنجاح.";
                return;
            }

            if (uri.Host.Equals("user", StringComparison.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/'), out var userId) || userId == Guid.Empty)
                    throw new InvalidOperationException("رابط الحساب غير صالح.");

                var currentUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
                if (currentUserId == Guid.Empty)
                {
                    var ownProfile = await _api.GetMyProfileAsync();
                    currentUserId = ownProfile.UserId;
                    if (currentUserId != Guid.Empty) _account.SetUserId(currentUserId);
                }

                var isSelf = currentUserId != Guid.Empty && currentUserId == userId;
                if (isSelf)
                {
                    await SwipePageTransition.NavigateAsync(this, "profile", SwipeDirection.Left);
                    return;
                }

                var publicProfile = await _api.GetPublicUserProfileAsync(userId);
                if (publicProfile.UserId != userId)
                    throw new InvalidOperationException("تعذر التحقق من الحساب المرتبط بالرمز.");
                var userName = string.IsNullOrWhiteSpace(publicProfile.Name)
                    ? (HimoAppLinkRouter.ReadDisplayName(uri) ?? "مستخدم Himo")
                    : publicProfile.Name.Trim();
                var directConversation = await _home.CreateConversationWithUserAsync(userName, userId);
                if (!Guid.TryParse(directConversation.RemoteId, out var directConversationId) || directConversationId == Guid.Empty)
                    throw new InvalidOperationException("تعذر إنشاء محادثة مع هذا الحساب.");
                await SwipePageTransition.NavigateAsync(
                    this,
                    $"chat?id={Uri.EscapeDataString(directConversationId.ToString("D"))}",
                    SwipeDirection.Left);
                return;
            }
            else
            {
                if (!Guid.TryParse(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/'), out var groupId) || groupId == Guid.Empty)
                    throw new InvalidOperationException("رابط المجموعة غير صالح.");
                var inviteCode = HimoAppLinkRouter.ReadQueryValue(uri, "invite");
                if (string.IsNullOrWhiteSpace(inviteCode) || inviteCode.Length is < 16 or > 80)
                    throw new InvalidOperationException("رمز دعوة المجموعة غير موجود أو غير صالح.");

                var groupRoute = $"group-invite?groupId={groupId:D}&invite={Uri.EscapeDataString(inviteCode)}";
                await SwipePageTransition.NavigateAsync(this, groupRoute, SwipeDirection.Left);
                return;
            }

        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                AppLocalization.T("LinkOpenFailed"),
                AppLocalization.TranslateSourceText(ex.Message),
                AppLocalization.T("OK"));
            ScannerStatusLabel.Text = "تعذر فتح الرابط. تحقق من الاتصال وحاول مرة أخرى.";
        }
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }
}
