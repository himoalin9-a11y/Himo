using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Himo.Views;

/// <summary>
/// Shows a verified group invitation preview. Opening this page never changes membership;
/// the server adds the current user only after the Join button is pressed.
/// </summary>
public sealed class GroupInvitePreviewPage : ContentPage, IQueryAttributable
{
    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private readonly Label _avatarInitial = new();
    private readonly Label _name = new();
    private readonly Label _description = new();
    private readonly Label _memberCount = new();
    private readonly Label _approvalNote = new();
    private readonly Label _status = new();
    private readonly ActivityIndicator _busy = new();
    private readonly Button _joinButton = new();
    private readonly Button _cancelButton = new();
    private Guid _groupId;
    private string _inviteCode = string.Empty;
    private HimoApiClient.GroupInvitePreviewResponse? _preview;
    private bool _loading;
    private bool _loaded;
    private bool _working;

    public GroupInvitePreviewPage(HimoApiClient api, HomeViewModel home)
    {
        _api = api;
        _home = home;
        Title = "دعوة مجموعة";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#080D22");
        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var groupText = ReadQuery(query, "groupId");
        _groupId = Guid.TryParse(groupText, out var id) ? id : Guid.Empty;
        _inviteCode = ReadQuery(query, "invite");
        _loaded = false;
        _preview = null;
    }

    private static string ReadQuery(IDictionary<string, object> query, string key)
    {
        if (!query.TryGetValue(key, out var value) || value is null)
            return string.Empty;
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        try { return Uri.UnescapeDataString(text).Trim(); }
        catch (UriFormatException) { return text.Trim(); }
    }

    private void BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star)
            }
        };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            Padding = new Thickness(16, 10),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(48), new(GridLength.Star), new(48)
            },
            ColumnSpacing = 8
        };
        var back = new Button
        {
            Text = "‹",
            FontSize = 30,
            Padding = 0,
            WidthRequest = 44,
            HeightRequest = 44,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.White
        };
        back.Clicked += CancelClicked;
        header.Add(back, 0, 0);

        var title = new Label
        {
            Text = "دعوة للانضمام إلى مجموعة",
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = Colors.White
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        header.Add(title, 1, 0);
        root.Add(header, 0, 0);

        var body = new VerticalStackLayout
        {
            Padding = new Thickness(22, 20, 22, 30),
            Spacing = 18,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };

        var card = new Border
        {
            Padding = new Thickness(22, 28),
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#46346F"),
            BackgroundColor = Color.FromArgb("#17152D"),
            StrokeShape = new RoundRectangle { CornerRadius = 26 },
            HorizontalOptions = LayoutOptions.Fill
        };
        card.SetDynamicResource(Border.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");

        var cardContent = new VerticalStackLayout
        {
            Spacing = 13,
            HorizontalOptions = LayoutOptions.Fill
        };
        var avatar = new Border
        {
            WidthRequest = 112,
            HeightRequest = 112,
            Padding = 0,
            StrokeThickness = 2,
            Stroke = Color.FromArgb("#B16CF0"),
            BackgroundColor = Color.FromArgb("#39215E"),
            StrokeShape = new Ellipse(),
            HorizontalOptions = LayoutOptions.Center,
            Content = _avatarInitial
        };
        _avatarInitial.Text = "م";
        _avatarInitial.FontSize = 42;
        _avatarInitial.FontAttributes = FontAttributes.Bold;
        _avatarInitial.HorizontalTextAlignment = TextAlignment.Center;
        _avatarInitial.VerticalTextAlignment = TextAlignment.Center;
        _avatarInitial.TextColor = Colors.White;
        cardContent.Add(avatar);

        _name.FontSize = 23;
        _name.FontAttributes = FontAttributes.Bold;
        _name.HorizontalTextAlignment = TextAlignment.Center;
        _name.TextColor = Colors.White;
        _name.Text = "جارٍ تحميل المجموعة...";
        _name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        cardContent.Add(_name);

        _memberCount.FontSize = 14;
        _memberCount.HorizontalTextAlignment = TextAlignment.Center;
        _memberCount.TextColor = Color.FromArgb("#BFB5D7");
        _memberCount.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        cardContent.Add(_memberCount);

        _description.FontSize = 14;
        _description.HorizontalTextAlignment = TextAlignment.Center;
        _description.VerticalTextAlignment = TextAlignment.Center;
        _description.LineBreakMode = LineBreakMode.WordWrap;
        _description.Text = "تمت دعوتك للانضمام إلى هذه المجموعة على Himo.";
        _description.TextColor = Color.FromArgb("#D1C9E2");
        _description.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        cardContent.Add(_description);

        _approvalNote.FontSize = 12;
        _approvalNote.HorizontalTextAlignment = TextAlignment.Center;
        _approvalNote.TextColor = Color.FromArgb("#F3C77A");
        _approvalNote.IsVisible = false;
        cardContent.Add(_approvalNote);

        _busy.IsRunning = true;
        _busy.IsVisible = true;
        _busy.Color = Color.FromArgb("#B16CF0");
        _busy.HorizontalOptions = LayoutOptions.Center;
        cardContent.Add(_busy);

        _status.FontSize = 12;
        _status.HorizontalTextAlignment = TextAlignment.Center;
        _status.TextColor = Color.FromArgb("#F2A3A3");
        _status.IsVisible = false;
        cardContent.Add(_status);

        card.Content = cardContent;
        body.Add(card);

        _joinButton.Text = "انضمام للمجموعة";
        _joinButton.FontSize = 16;
        _joinButton.FontAttributes = FontAttributes.Bold;
        _joinButton.HeightRequest = 54;
        _joinButton.CornerRadius = 18;
        _joinButton.TextColor = Colors.White;
        _joinButton.BackgroundColor = Color.FromArgb("#7D42D8");
        _joinButton.IsEnabled = false;
        _joinButton.Clicked += JoinClicked;
        body.Add(_joinButton);

        _cancelButton.Text = "إلغاء";
        _cancelButton.FontSize = 15;
        _cancelButton.HeightRequest = 50;
        _cancelButton.CornerRadius = 18;
        _cancelButton.TextColor = Colors.White;
        _cancelButton.BackgroundColor = Color.FromArgb("#302944");
        _cancelButton.Clicked += CancelClicked;
        body.Add(_cancelButton);

        root.Add(new ScrollView
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        }, 0, 1);
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded && !_loading)
            await LoadPreviewAsync();
    }

    private async Task LoadPreviewAsync()
    {
        if (_loading) return;
        _loading = true;
        _busy.IsVisible = _busy.IsRunning = true;
        _joinButton.IsEnabled = false;
        _status.IsVisible = false;
        try
        {
            if (_groupId == Guid.Empty || _inviteCode.Length is < 16 or > 80)
                throw new InvalidOperationException("رابط دعوة المجموعة غير صالح.");

            await _api.TokenInitialization;
            if (!_api.HasToken)
                throw new InvalidOperationException("سجّل الدخول إلى Himo لعرض دعوة المجموعة.");

            var preview = await _api.GetGroupInvitePreviewAsync(_groupId, _inviteCode);
            if (preview.Id != _groupId)
                throw new InvalidOperationException("رمز الدعوة لا يطابق المجموعة الموجودة في الرابط.");

            _preview = preview;
            _name.Text = string.IsNullOrWhiteSpace(preview.Name) ? "مجموعة Himo" : preview.Name;
            _avatarInitial.Text = _name.Text.Trim().Length > 0 ? _name.Text.Trim()[0].ToString() : "م";
            _memberCount.Text = preview.MemberCount == 1 ? "عضو واحد" : $"{preview.MemberCount} أعضاء";
            _description.Text = string.IsNullOrWhiteSpace(preview.Description)
                ? "تمت دعوتك للانضمام إلى هذه المجموعة على Himo."
                : preview.Description;
            var approvalRequired = preview.RequiresAdminApproval && !preview.IsMember;
            _approvalNote.IsVisible = approvalRequired;
            _approvalNote.Text = approvalRequired
                ? "يتطلب الانضمام موافقة مشرف المجموعة. لا يمكن إتمام الانضمام من هذا الرابط."
                : string.Empty;
            _joinButton.Text = preview.IsMember
                ? "فتح المجموعة"
                : approvalRequired ? "يتطلب موافقة المشرف" : "انضمام للمجموعة";
            _joinButton.IsEnabled = preview.IsMember || !preview.RequiresAdminApproval;
            _loaded = true;
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.IsVisible = true;
            _joinButton.IsEnabled = false;
            System.Diagnostics.Debug.WriteLine($"[Himo GroupInvite] Preview failed: {ex}");
        }
        finally
        {
            _busy.IsRunning = _busy.IsVisible = false;
            _loading = false;
        }
    }

    private async void JoinClicked(object? sender, EventArgs e)
    {
        if (_working || _preview is null) return;
        _working = true;
        _joinButton.IsEnabled = _cancelButton.IsEnabled = false;
        _joinButton.Text = _preview.IsMember ? "جارٍ فتح المجموعة..." : "جارٍ الانضمام...";
        try
        {
            Guid conversationId;
            if (_preview.IsMember)
            {
                // Existing members can open the conversation directly. Do not call
                // the join endpoint again (approval-only groups reject new joins).
                conversationId = _groupId;
            }
            else
            {
                if (_preview.RequiresAdminApproval)
                    throw new InvalidOperationException("يتطلب الانضمام موافقة مشرف المجموعة.");

                // This is the only point in the link flow that invokes the join API.
                var conversation = await _home.JoinGroupByInviteAsync(_inviteCode, _groupId);
                if (!Guid.TryParse(conversation.RemoteId, out conversationId) || conversationId != _groupId)
                    throw new InvalidOperationException("تعذر فتح المجموعة المرتبطة بالدعوة.");
            }

            var shell = Shell.Current;
            if (shell is null)
                throw new InvalidOperationException("تعذر فتح واجهة المحادثة داخل التطبيق.");
            await shell.GoToAsync($"chat?id={Uri.EscapeDataString(conversationId.ToString("D"))}", false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تعذر الانضمام إلى المجموعة"), AppLocalization.TranslateSourceText(ex.Message),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            _joinButton.Text = _preview.IsMember ? "فتح المجموعة" : "انضمام للمجموعة";
        }
        finally
        {
            _working = false;
            _joinButton.IsEnabled = _preview is not null &&
                (_preview.IsMember || !_preview.RequiresAdminApproval);
            _cancelButton.IsEnabled = true;
        }
    }

    private async void CancelClicked(object? sender, EventArgs e)
    {
        if (_working) return;
        try
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("..", false);
            else if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo GroupInvite] Cancel navigation failed: {ex.Message}");
        }
    }
}
