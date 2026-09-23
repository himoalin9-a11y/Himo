using Himo.Models;
using Himo.Services;
using Himo.ViewModels;

namespace Himo.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _vm;
    private readonly INotificationService _notifications;
    private readonly HimoApiClient _api;
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _hasNotificationBaseline;
    public HomePage(HomeViewModel vm, INotificationService notifications, HimoApiClient api)
    {
        InitializeComponent();
        _vm = vm;
        _notifications = notifications;
        _api = api;
        BindingContext = vm;
    }

    private void SearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _vm.SearchText = e.NewTextValue ?? "";
        _vm.Filter();
        UpdateEmptyState();
    }

    private async void ConversationSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Conversation conversation)
        {
            ((CollectionView)sender).SelectedItem = null;
            var shell = Shell.Current;
            if (shell is null) return;
            await shell.GoToAsync($"chat?id={conversation.Id}");
        }
    }

    private async void NewConversationClicked(object sender, EventArgs e)
    {
        // A conversation without another participant is not useful for the
        // current server model. Start the supported user-search flow instead
        // of creating a local-only or empty conversation.
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("search");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _notifications.InitializeAsync();
#if ANDROID
        Himo.MainActivity.RequestNotificationPermissionIfNeeded();
#endif
        UpdateEmptyState();
        await RefreshAsync(showNotifications: true);
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        StopPolling();
        base.OnDisappearing();
    }

    private async Task RefreshAsync(bool showNotifications)
    {
        if (!await _refreshGate.WaitAsync(0)) return;

        try
        {
            if (RefreshIndicator is not null)
            {
                RefreshIndicator.IsVisible = true;
                RefreshIndicator.IsRunning = true;
            }

            var before = _vm.Conversations
                .GroupBy(x => x.RemoteId ?? $"local:{x.Id}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(x => x.UnreadCount), StringComparer.OrdinalIgnoreCase);

            var connected = await _vm.RefreshFromServerAsync();
            UpdateEmptyState();
            ConnectionLabel?.Text = connected ? "متصل بالخادم" : "غير متصل بالخادم";
            RetryButton?.SetValue(IsVisibleProperty, !connected);

            if (!connected)
            {
                // Do not compare unread counts across an offline period; after
                // reconnecting the current state becomes the new baseline.
                _hasNotificationBaseline = false;
                return;
            }

            if (!showNotifications || !_notifications.IsEnabled) return;

            if (!_hasNotificationBaseline)
            {
                _hasNotificationBaseline = true;
                return;
            }

            foreach (var conversation in _vm.Conversations)
            {
                var key = conversation.RemoteId ?? $"local:{conversation.Id}";
                var previousUnread = before.TryGetValue(key, out var unread) ? unread : 0;

                if (conversation.UnreadCount > previousUnread && conversation.UnreadCount > 0)
                {
                    await _notifications.ShowMessageAsync(
                        conversation.Name,
                        conversation.LastMessage,
                        conversation.Id.ToString());
                }
            }
        }
        finally
        {
            if (RefreshIndicator is not null)
            {
                RefreshIndicator.IsRunning = false;
                RefreshIndicator.IsVisible = false;
            }
            _refreshGate.Release();
        }
    }


    private void UpdateEmptyState()
    {
        var hasSearch = !string.IsNullOrWhiteSpace(_vm.SearchText);
        var hasResults = _vm.FilteredConversations.Count > 0;

        if (EmptyTitle is not null)
            EmptyTitle.Text = hasSearch && !hasResults ? "لا توجد نتائج" : "لا توجد محادثات بعد";

        if (EmptyHint is not null)
            EmptyHint.Text = hasSearch && !hasResults
                ? "جرّب كلمة بحث أخرى."
                : "اضغط ＋ للبحث عن مستخدم وبدء محادثة جديدة.";
    }

    private void StartPolling()
    {
        StopPolling();
        if (!_vm.HasServerSession) return;
        _pollCts = new CancellationTokenSource();
        _ = PollAsync(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                await RefreshAsync(showNotifications: true);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep the home screen usable when the network is temporarily unavailable.
            }
        }
    }


    private async void ConversationRefresh_Refreshing(object sender, EventArgs e)
    {
        try
        {
            await RefreshAsync(showNotifications: false);
        }
        finally
        {
            ConversationRefresh?.IsRefreshing = false;
        }
    }

    private async void RetryClicked(object sender, EventArgs e)
    {
        Button? button = sender as Button;
        var originalText = button?.Text;
        if (button is not null)
        {
            button.IsEnabled = false;
            button.Text = "جارٍ الاتصال...";
        }

        try
        {
            await RefreshAsync(showNotifications: false);
        }
        finally
        {
            if (button is not null)
            {
                button.Text = originalText ?? "إعادة المحاولة";
                button.IsEnabled = true;
            }
        }
    }

    private async void SearchUserClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("search");
    }

    private async void SettingsClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("settings");
    }
}
