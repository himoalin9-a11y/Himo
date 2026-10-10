using Himo.Models;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace Himo.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _vm;
    private readonly HimoApiClient _api;
    private readonly ProfileService _profile;
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    public HomePage(HomeViewModel vm, HimoApiClient api, ProfileService profile)
    {
        InitializeComponent();
        _vm = vm;
        _api = api;
        _profile = profile;
        BindingContext = vm;
        DisplayOwnProfilePhoto(_profile.Profile.PhotoPath);
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
            var routeId = !string.IsNullOrWhiteSpace(conversation.RemoteId)
                ? conversation.RemoteId
                : conversation.Id.ToString();
            await shell.GoToAsync($"chat?id={routeId}", false);
        }
    }

    private async void NewConversationClicked(object sender, EventArgs e)
    {
        // A conversation without another participant is not useful for the
        // current server model. Start the supported user-search flow instead
        // of creating a local-only or empty conversation.
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("search", false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateEmptyState();
        DisplayOwnProfilePhoto(_profile.Profile.PhotoPath);
        StartPolling();
        // Refresh the local account avatar independently of the conversation list,
        // so the home page can render immediately while the server request runs.
        _ = RefreshOwnProfilePhotoAsync();
        // Let the first frame render before notification/network work.
        _ = InitializeHomeAsync();
    }


    private void DisplayOwnProfilePhoto(string? path)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        HomeProfilePhotoImage.Source = hasPhoto ? ImageSource.FromFile(path!) : null;
        HomeProfilePhotoImage.IsVisible = hasPhoto;
        HomeProfilePlaceholderIcon.IsVisible = !hasPhoto;
    }

    private async Task RefreshOwnProfilePhotoAsync()
    {
        try
        {
            await _api.TokenInitialization;
            if (!_api.HasToken) return;

            var profile = await _api.GetMyProfileAsync();
            if (profile.HasProfilePhoto == true)
            {
                var photo = await _api.GetMyProfilePhotoAsync();
                if (photo is null) return;

                var path = _profile.SavePhoto(photo.Bytes, photo.ContentType);
                await MainThread.InvokeOnMainThreadAsync(() => DisplayOwnProfilePhoto(path));
            }
            else if (profile.HasProfilePhoto == false)
            {
                _profile.ClearPhoto();
                await MainThread.InvokeOnMainThreadAsync(() => DisplayOwnProfilePhoto(null));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Home] Profile avatar refresh failed: {ex.Message}");
            // Keep a locally saved avatar if the server is temporarily unavailable.
        }
    }

    private async Task InitializeHomeAsync()
    {
        await Task.Yield();
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Home] Initial refresh failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        StopPolling();
        base.OnDisappearing();
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;

        try
        {
            if (RefreshIndicator is not null)
            {
                RefreshIndicator.IsVisible = true;
                RefreshIndicator.IsRunning = true;
            }

            var connected = await _vm.RefreshFromServerAsync();
            UpdateEmptyState();
            ConnectionLabel?.Text = connected ? "متصل بالخادم" : "غير متصل بالخادم";
            RetryButton?.SetValue(IsVisibleProperty, !connected);

            if (!connected)
                return;
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
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                await RefreshAsync();
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
            await RefreshAsync();
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
            await RefreshAsync();
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

    private async void CreateGroupClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("CreateGroupPage");
    }

    private async void SearchUserClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("search", false);
    }

    private void OverflowMenuClicked(object sender, EventArgs e)
    {
        if (OverflowMenu is not null)
            OverflowMenu.IsVisible = !OverflowMenu.IsVisible;
    }

    private async void SettingsClicked(object sender, EventArgs e)
    {
        if (OverflowMenu is not null)
            OverflowMenu.IsVisible = false;

        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("settings", false);
    }

    private async void ProfileClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("profile", false);
    }
}
