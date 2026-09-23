using Himo.Services;

namespace Himo.Views;

public partial class AppLockPage : ContentPage
{
    private readonly AppLockService _lock;
    private readonly Func<Task> _unlocked;
    private int _busy;

    public AppLockPage(AppLockService lockService, Func<Task> unlocked)
    {
        InitializeComponent();
        _lock = lockService;
        _unlocked = unlocked;
    }

    private async void UnlockClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            var pin = PinEntry?.Text?.Trim() ?? string.Empty;
            if (await _lock.VerifyPinAsync(pin))
            {
                if (UnlockButton is not null) UnlockButton.IsEnabled = false;
                if (ErrorLabel is not null) ErrorLabel.Text = string.Empty;
                await _unlocked();
                return;
            }

            if (ErrorLabel is not null) ErrorLabel.Text = "رمز القفل غير صحيح.";
            if (PinEntry is not null)
            {
                PinEntry.Text = string.Empty;
                PinEntry.Focus();
            }
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}
