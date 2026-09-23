using Himo.Services;

namespace Himo.Views;

public partial class SearchPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly ViewModels.HomeViewModel _home;
    private bool _busy;

    public SearchPage(HimoApiClient api, ViewModels.HomeViewModel home)
    {
        InitializeComponent();
        _api = api;
        _home = home;
    }

    private async Task SearchAsync()
    {
        if (_busy) return;
        try
        {
            _busy = true;
            if (SearchButton is not null) SearchButton.IsEnabled = false;
            if (SearchProgress is not null) SearchProgress.IsVisible = true;

            var email = EmailEntry?.Text?.Trim() ?? string.Empty;
            if (!IsValidEmail(email))
            {
                await DisplayAlertAsync("البحث", "أدخل بريدًا إلكترونيًا صحيحًا.", "حسنًا");
                return;
            }
            if (Results is not null) Results.ItemsSource = await _api.SearchUsersAsync(email);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("البحث", ex.Message, "حسنًا");
        }
        finally
        {
            _busy = false;
            if (SearchButton is not null) SearchButton.IsEnabled = true;
            if (SearchProgress is not null) SearchProgress.IsVisible = false;
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..");
    }

    private async void SearchClicked(object sender, EventArgs e)
    {
        await SearchAsync();
    }

    private async void SearchCompleted(object sender, EventArgs e)
    {
        await SearchAsync();
    }

    private static bool IsValidEmail(string value)
    {
        var email = value.Trim();
        if (email.Length is < 5 or > 254 || email.Contains(' ')) return false;
        var at = email.LastIndexOf('@');
        return at > 0 && at < email.Length - 1 && email[(at + 1)..].Contains('.');
    }

    private async void UserSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_busy) return;
        if (e.CurrentSelection.FirstOrDefault() is not Models.UserSearchDto user) return;
        _busy = true;
        ((CollectionView)sender).SelectedItem = null;
        try
        {
            var conversation = await _home.CreateConversationWithUserAsync(user.Name, user.Id);
            var shell = Shell.Current;
            if (shell is null) return;
            await shell.GoToAsync($"chat?id={conversation.Id}");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("المحادثة", ex.Message, "حسنًا");
        }
        finally
        {
            _busy = false;
        }
    }
}
