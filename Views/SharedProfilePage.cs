using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>Read-only public profile opened from a Himo share link.</summary>
public sealed class SharedProfilePage : ContentPage, IQueryAttributable
{
    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private readonly Image _photo;
    private readonly Label _initial;
    private readonly Label _name;
    private readonly Label _status;
    private readonly Label _loading;
    private readonly Button _messageButton;
    private Guid _userId;
    private bool _loadStarted;
    private bool _isReady;
    private string _displayName = string.Empty;

    public SharedProfilePage(HimoApiClient api, HomeViewModel home)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _home = home ?? throw new ArgumentNullException(nameof(home));

        Title = AppLocalization.T("Profile");
        FlowDirection = AppLocalization.PreferredFlowDirection;
        BackgroundColor = Color.FromArgb("#10071A");
        Shell.SetNavBarIsVisible(this, false);

        var backButton = new Button
        {
            Text = "‹",
            FontSize = 32,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            CornerRadius = 15,
            BackgroundColor = Color.FromArgb("#2A1740"),
            TextColor = Color.FromArgb("#D8B4FF"),
            HorizontalOptions = LayoutOptions.Start
        };
        backButton.Clicked += async (_, _) =>
        {
            if (Shell.Current is { } shell)
                await shell.GoToAsync("..");
        };

        var heading = new Label
        {
            Text = AppLocalization.T("Profile"),
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        _photo = new Image
        {
            Aspect = Aspect.AspectFill,
            IsVisible = false
        };
        _initial = new Label
        {
            Text = "H",
            FontSize = 42,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        var avatarContent = new Grid();
        avatarContent.Children.Add(_initial);
        avatarContent.Children.Add(_photo);
        var avatar = new Border
        {
            WidthRequest = 132,
            HeightRequest = 132,
            Padding = 0,
            Stroke = new SolidColorBrush(Color.FromArgb("#B66BFF")),
            StrokeThickness = 2,
            BackgroundColor = Color.FromArgb("#321B4B"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(66) },
            HorizontalOptions = LayoutOptions.Center,
            Content = avatarContent
        };

        _name = new Label
        {
            Text = AppLocalization.T("HimoUser"),
            FontSize = 23,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _status = new Label
        {
            Text = AppLocalization.T("Loading"),
            FontSize = 14,
            TextColor = Color.FromArgb("#C8B5DC"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        _loading = new Label
        {
            Text = AppLocalization.T("Loading"),
            FontSize = 12,
            TextColor = Color.FromArgb("#B79CCF"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        _messageButton = new Button
        {
            Text = AppLocalization.T("StartConversation"),
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#872BE0"),
            BorderColor = Color.FromArgb("#BD82FF"),
            BorderWidth = 1,
            CornerRadius = 18,
            HeightRequest = 52,
            HorizontalOptions = LayoutOptions.Fill,
            IsEnabled = false
        };
        _messageButton.Clicked += MessageClicked;

        var card = new Border
        {
            Padding = new Thickness(22),
            BackgroundColor = Color.FromArgb("#1A0E2B"),
            Stroke = new SolidColorBrush(Color.FromArgb("#573477")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(26) },
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children = { avatar, _name, _status, _loading, _messageButton }
            }
        };

        var header = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };
        header.Children.Add(backButton);
        header.Children.Add(heading);
        Grid.SetColumn(heading, 1);
        heading.HorizontalOptions = LayoutOptions.Center;

        var body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(0, 30, 0, 24),
                VerticalOptions = LayoutOptions.Center,
                Children = { card }
            },
            VerticalOptions = LayoutOptions.Fill
        };
        Grid.SetRow(body, 1);

        var root = new Grid
        {
            Padding = new Thickness(18, 12),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            }
        };
        root.Children.Add(header);
        root.Children.Add(body);
        Content = root;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("userId", out var rawId) &&
            Guid.TryParse(Convert.ToString(rawId), out var parsedId) && parsedId != Guid.Empty)
            _userId = parsedId;

        if (query.TryGetValue("name", out var rawName))
            _displayName = Convert.ToString(rawName)?.Trim() ?? string.Empty;

        if (_userId != Guid.Empty && !_loadStarted)
        {
            _loadStarted = true;
            _ = LoadProfileAsync();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        FlowDirection = AppLocalization.PreferredFlowDirection;
        AppLocalization.AttachToPage(this);
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            _loading.IsVisible = true;
            _status.Text = AppLocalization.T("Loading");
            var profile = await _api.GetPublicUserProfileAsync(_userId);
            _displayName = string.IsNullOrWhiteSpace(profile.Name)
                ? (_displayName.Length > 0 ? _displayName : AppLocalization.T("HimoUser"))
                : profile.Name.Trim();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _name.AutomationId = string.IsNullOrWhiteSpace(_displayName)
                    ? string.Empty
                    : "Himo.UserSuppliedText";
                _name.Text = _displayName;
                _initial.Text = GetInitial(_displayName);
                _status.AutomationId = string.IsNullOrWhiteSpace(profile.Status)
                    ? string.Empty
                    : "Himo.UserSuppliedText";
                _status.Text = string.IsNullOrWhiteSpace(profile.Status)
                    ? AppLocalization.T("AvailableOnHimo")
                    : profile.Status;
                _messageButton.IsEnabled = true;
                _loading.IsVisible = false;
                _isReady = true;
            });

            if (profile.HasProfilePhoto)
            {
                var photo = await _api.GetUserProfilePhotoAsync(_userId);
                if (photo is not null)
                {
                    var bytes = photo.Bytes;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        _photo.Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
                        _photo.IsVisible = true;
                        _initial.IsVisible = false;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo SharedProfile] Load failed: {ex}");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _loading.IsVisible = false;
                _status.AutomationId = string.Empty;
                _status.Text = AppLocalization.T("NotAvailable");
                _messageButton.IsEnabled = false;
            });
        }
    }

    private async void MessageClicked(object? sender, EventArgs e)
    {
        if (!_isReady || _userId == Guid.Empty)
            return;

        _messageButton.IsEnabled = false;
        try
        {
            var conversation = await _home.CreateConversationWithUserAsync(_displayName, _userId);
            if (!Guid.TryParse(conversation.RemoteId, out var conversationId) || conversationId == Guid.Empty)
                throw new InvalidOperationException(AppLocalization.T("ConversationOpenFailed"));

            var shell = Shell.Current;
            if (shell is null)
                return;
            await shell.GoToAsync($"chat?id={Uri.EscapeDataString(conversationId.ToString("D"))}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo SharedProfile] Start conversation failed: {ex}");
            await DisplayAlertAsync(AppLocalization.T("Error"), AppLocalization.TranslateSourceText(ex.Message), AppLocalization.T("OK"));
        }
        finally
        {
            _messageButton.IsEnabled = _isReady;
        }
    }

    private static string GetInitial(string name)
    {
        var first = name.Trim().FirstOrDefault(char.IsLetterOrDigit);
        return first == default ? "H" : first.ToString();
    }
}
