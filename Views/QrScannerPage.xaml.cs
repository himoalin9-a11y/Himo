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
            await HandleProfileLinkAsync(incomingLink);
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
                await DisplayAlertAsync("إذن الكاميرا", "فعّل إذن الكاميرا لـ Himo ثم حاول مجددًا.", "حسنًا");
                return;
            }
        }
        catch (Exception ex)
        {
            ScannerStatusLabel.Text = "تعذر تشغيل الكاميرا.";
            await DisplayAlertAsync("الكاميرا", ex.Message, "حسنًا");
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
            ScannerStatusLabel.Text = "وجّه الكاميرا إلى رمز QR الخاص بحساب Himo.";
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
                await HandleProfileLinkAsync(value);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo QR] Scan handling failed: {ex}");
                try { await DisplayAlertAsync("تعذر قراءة الرمز", "لم نتمكن من معالجة هذا الرمز. وجّه الكاميرا إلى رمز Himo وحاول مجددًا.", "حسنًا"); }
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

    private async Task HandleProfileLinkAsync(string value)
    {
        if (!TryParseProfileLink(value, out var userId, out var userName))
        {
            await DisplayAlertAsync("رمز غير صالح", "هذا الرمز لا يحتوي على رابط حساب Himo صالح.", "حسنًا");
            ScannerStatusLabel.Text = "جرّب مسح رمز QR لحساب Himo.";
            return;
        }

        try { await _api.TokenInitialization; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo QR] Token initialization failed: {ex.Message}"); }

        if (!_api.HasToken)
        {
            await DisplayAlertAsync("تسجيل الدخول", "سجّل الدخول إلى Himo قبل فتح حساب من رمز QR.", "حسنًا");
            return;
        }

        var currentUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
        if (currentUserId == Guid.Empty)
        {
            try
            {
                var ownProfile = await _api.GetMyProfileAsync();
                if (ownProfile.UserId != Guid.Empty)
                {
                    currentUserId = ownProfile.UserId;
                    _account.SetUserId(currentUserId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo QR] Could not resolve current user id: {ex.Message}");
            }
        }

        if (currentUserId != Guid.Empty && currentUserId == userId)
        {
            await DisplayAlertAsync("هذا حسابك", "رمز QR يعود إلى حسابك الحالي.", "حسنًا");
            ScannerStatusLabel.Text = "يمكنك مسح رمز حساب شخص آخر.";
            return;
        }

        var confirmed = await DisplayAlertAsync("تم العثور على حساب", $"هل تريد بدء محادثة مع {userName}؟", "بدء المحادثة", "إلغاء");
        if (!confirmed)
        {
            ScannerStatusLabel.Text = "يمكنك مسح رمز آخر في أي وقت.";
            return;
        }

        try
        {
            var conversation = await _home.CreateConversationWithUserAsync(userName, userId);
            var routeId = !string.IsNullOrWhiteSpace(conversation.RemoteId)
                ? conversation.RemoteId
                : conversation.Id.ToString();
            if (Shell.Current is not null)
                await SwipePageTransition.NavigateAsync(this, $"chat?id={Uri.EscapeDataString(routeId)}", SwipeDirection.Left);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر فتح الحساب", ex.Message, "حسنًا");
            ScannerStatusLabel.Text = "لم نتمكن من فتح هذا الحساب. تحقق من الاتصال ثم جرّب مرة أخرى.";
        }
    }

    private static bool TryParseProfileLink(string value, out Guid userId, out string name)
    {
        userId = Guid.Empty;
        name = "حساب Himo";
        // QR codes generated by ShareAccountPage use himo://user/{guid}. Trim
        // scanner whitespace and validate the exact scheme/host/path before use.
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("himo", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("user", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(Uri.UnescapeDataString(uri.AbsolutePath).Trim('/'), out userId))
            return false;

        var query = uri.Query.TrimStart('?');
        foreach (var item in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (pair.Length != 2) continue;
            try
            {
                string decoded;
                if (pair[0].Equals("name64", StringComparison.OrdinalIgnoreCase))
                {
                    var token = pair[1].Replace('-', '+').Replace('_', '/');
                    token = token.PadRight(token.Length + ((4 - token.Length % 4) % 4), '=');
                    decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token)).Trim();
                }
                else if (pair[0].Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    decoded = Uri.UnescapeDataString(pair[1].Replace('+', ' ')).Trim();
                }
                else continue;

                if (!string.IsNullOrWhiteSpace(decoded) && decoded.Length <= 60)
                    name = decoded;
            }
            catch (FormatException)
            {
                // An invalid optional display-name token falls back to a generic account name.
            }
        }
        return true;
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }
}
