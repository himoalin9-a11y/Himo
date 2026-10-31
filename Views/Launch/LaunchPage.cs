using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Himo.Views.Launch;

/// <summary>
/// Native welcome/launch screen. It reuses the project's existing saucer drawable
/// and hands off to the normal login, app-lock, or home flow after the launch animation.
/// </summary>
public sealed class LaunchPage : ContentPage
{
    private readonly Func<Task> _continue;
    private readonly SaucerLaunchDrawable _drawable = new();
    private readonly GraphicsView _scene;
    private readonly Label _hint;
    private IDispatcherTimer? _timer;
    private DateTime _lastTickUtc;
    private bool _completed;

    public LaunchPage(Func<Task> continueAction)
    {
        _continue = continueAction ?? throw new ArgumentNullException(nameof(continueAction));
        NavigationPage.SetHasNavigationBar(this, false);
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Color.FromArgb("#02040D");
        FlowDirection = FlowDirection.RightToLeft;

        _scene = new GraphicsView
        {
            Drawable = _drawable,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };

        _hint = new Label
        {
            Text = "اضغط على المركبة للانطلاق",
            TextColor = Color.FromArgb("#E8E2FF"),
            FontSize = 15,
            FontAttributes = FontAttributes.None,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            BackgroundColor = Color.FromArgb("#66150B27"),
            Padding = new Thickness(22, 11),
            CharacterSpacing = .25
        };

        var footer = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Padding = new Thickness(20, 0, 20, 48),
            Spacing = 10,
            Children =
            {
                new Label
                {
                    Text = "HIMO",
                    TextColor = Color.FromArgb("#FFFFFF"),
                    FontSize = 27,
                    FontAttributes = FontAttributes.Bold,
                    CharacterSpacing = 5,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                _hint
            }
        };

        var root = new Grid
        {
            BackgroundColor = Color.FromArgb("#02040D"),
            Children = { _scene, footer }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnSceneTapped;
        root.GestureRecognizers.Add(tap);
        Content = root;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _lastTickUtc = DateTime.UtcNow;
        _timer ??= CreateTimer();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        base.OnDisappearing();
    }

    private IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.IsRepeating = true;
        timer.Tick += OnFrame;
        return timer;
    }

    private void OnSceneTapped(object? sender, TappedEventArgs e) => BeginLaunch();

    private void BeginLaunch()
    {
        if (_drawable.IsLaunching || _completed)
            return;

        _drawable.IsLaunching = true;
        _drawable.LaunchSeconds = 0;
        _hint.Text = "استعد للإقلاع";
        _lastTickUtc = DateTime.UtcNow;
        _scene.Invalidate();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_completed)
            return;

        var now = DateTime.UtcNow;
        var delta = Math.Clamp((now - _lastTickUtc).TotalSeconds, 0, .075);
        _lastTickUtc = now;

        if (_drawable.IsLaunching)
        {
            _drawable.LaunchSeconds += delta;
            if (_drawable.LaunchSeconds >= 4.35)
            {
                _completed = true;
                _timer?.Stop();
                _ = ContinueAsync();
            }
        }
        else
        {
            _drawable.IdleSeconds += delta;
            // A missed tap must never leave the user trapped on the welcome screen.
            if (_drawable.IdleSeconds >= 7.0)
                BeginLaunch();
        }

        _scene.Invalidate();
    }

    private async Task ContinueAsync()
    {
        try
        {
            await _continue();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Welcome] Continue failed: {ex}");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _completed = false;
                _drawable.IsLaunching = false;
                _drawable.LaunchSeconds = 0;
                _hint.Text = "تعذر المتابعة. اضغط للمحاولة مجددًا";
                _timer?.Start();
            });
        }
    }
}
