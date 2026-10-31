using Himo.Models;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>Final step for creating a group.</summary>
public sealed class GroupSettingsPage : ContentPage
{
    private static readonly (string Title, int Seconds)[] Timers =
    {
        ("إيقاف", 0),
        ("ساعة واحدة", 3600),
        ("ساعتان", 7200),
        ("24 ساعة", 86400),
        ("5 أيام", 432000),
        ("30 يومًا", 2592000)
    };

    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private readonly GroupDraft _draft;
    private Entry _nameEntry = new();
    private Picker _timerPicker = new();
    private Label _permissionSummary = new();
    private Button _createButton = new();
    private ActivityIndicator _busy = new();
    private readonly Dictionary<Guid, (Image Image, Label Initial)> _memberAvatarViews = new();
    private readonly HashSet<Guid> _memberPhotoLoadsStarted = new();
    private bool _creating;

    public GroupSettingsPage(HimoApiClient api, HomeViewModel home, GroupDraft draft)
    {
        _api = api;
        _home = home;
        _draft = draft;
        Title = "إعداد المجموعة";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    private async Task LoadMemberPhotosAsync()
    {
        var members = _draft.Members
            .Where(x => x.HasProfilePhoto && string.IsNullOrWhiteSpace(x.PhotoPath) && _memberPhotoLoadsStarted.Add(x.UserId))
            .ToArray();
        if (members.Length == 0) return;

        using var gate = new SemaphoreSlim(3, 3);
        var tasks = members.Select(async member =>
        {
            await gate.WaitAsync();
            try
            {
                var path = await ProfilePhotoCache.GetPathAsync(
                    _api, member.UserId, true, member.ProfilePhotoVersion);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    member.PhotoPath = path;
                    if (_memberAvatarViews.TryGetValue(member.UserId, out var avatar))
                    {
                        avatar.Image.Source = ImageSource.FromFile(path);
                        avatar.Image.IsVisible = true;
                        avatar.Initial.IsVisible = false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Groups] Settings avatar load failed for {member.UserId}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    private void BuildPage()
    {
        var root = new Grid
        {
            Padding = new Thickness(18, 10, 18, 14),
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) },
            RowSpacing = 12
        };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var body = new VerticalStackLayout { Spacing = 15, Padding = new Thickness(0, 0, 0, 10) };
        body.Add(CreateHeader());
        body.Add(CreateGroupPreview());

        body.Add(MakeSectionTitle("اسم المجموعة"));
        _nameEntry = new Entry
        {
            Placeholder = "اكتب اسم المجموعة",
            MaxLength = 60,
            FontSize = 15,
            Text = _draft.Name,
            ReturnType = ReturnType.Done,
            BackgroundColor = Colors.Transparent,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _nameEntry.SetDynamicResource(Entry.TextColorProperty, "HimoPrimary");
        _nameEntry.SetDynamicResource(Entry.PlaceholderColorProperty, "HimoTextSecondary");
        var nameCard = CreateFieldCard(_nameEntry, 58);
        body.Add(nameCard);

        body.Add(MakeSectionTitle("الرسائل ذاتية الاختفاء"));
        _timerPicker = new Picker
        {
            Title = "مدة الاختفاء",
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent
        };
        _timerPicker.SetDynamicResource(Picker.TextColorProperty, "HimoPrimary");
        _timerPicker.SetDynamicResource(Picker.TitleColorProperty, "HimoTextSecondary");
        foreach (var option in Timers) _timerPicker.Items.Add(option.Title);
        var timerIndex = Array.FindIndex(Timers, x => x.Seconds == _draft.DisappearingSeconds);
        _timerPicker.SelectedIndex = timerIndex < 0 ? 0 : timerIndex;
        _timerPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_timerPicker.SelectedIndex >= 0 && _timerPicker.SelectedIndex < Timers.Length)
                _draft.DisappearingSeconds = Timers[_timerPicker.SelectedIndex].Seconds;
        };
        body.Add(CreateFieldCard(_timerPicker, 58));

        body.Add(MakeSectionTitle("إدارة المجموعة"));
        var permissionStack = new VerticalStackLayout { Spacing = 0 };
        var permissionContent = new Grid
        {
            Padding = new Thickness(15, 14),
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(26) },
            ColumnSpacing = 10
        };
        var permissionLabels = new VerticalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
        permissionLabels.Add(MakeLabel("أذونات المجموعة", 15, true, "HimoPrimary"));
        _permissionSummary = MakeLabel(string.Empty, 11.5, false, "HimoTextSecondary");
        permissionLabels.Add(_permissionSummary);
        permissionContent.Add(permissionLabels, 0, 0);
        var chevron = MakeLabel("‹", 25, true, "HimoAccent");
        chevron.HorizontalTextAlignment = TextAlignment.Center;
        permissionContent.Add(chevron, 1, 0);
        var permissionCard = MakeCard(permissionContent);
        permissionCard.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await OpenPermissionsAsync())
        });
        body.Add(permissionCard);

        body.Add(MakeSectionTitle($"الأعضاء · {_draft.Members.Count}"));
        body.Add(CreateMembersStrip());

        root.Add(new ScrollView
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        }, 0, 0);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(26), new(GridLength.Star) },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 4, 0, 0)
        };
        _busy = new ActivityIndicator { IsVisible = false, IsRunning = false, VerticalOptions = LayoutOptions.Center };
        _busy.SetDynamicResource(ActivityIndicator.ColorProperty, "HimoAccent");
        footer.Add(_busy, 0, 0);
        _createButton = MakePrimaryButton("إنشاء المجموعة", 52);
        _createButton.Clicked += CreateGroupClicked;
        footer.Add(_createButton, 1, 0);
        root.Add(footer, 0, 1);

        Content = root;
        UpdatePermissionSummary();
    }

    private View CreateHeader()
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(46), new(GridLength.Star), new(46) },
            HeightRequest = 50,
            ColumnSpacing = 8
        };
        var back = MakeBackButton();
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        var title = MakeLabel("إعداد المجموعة", 20, true, "HimoPrimary");
        title.HorizontalTextAlignment = TextAlignment.Center;
        header.Add(title, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0);
        return header;
    }

    private View CreateGroupPreview()
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(58), new(GridLength.Star) },
            ColumnSpacing = 12,
            Padding = new Thickness(15, 14)
        };
        var icon = new Border
        {
            WidthRequest = 56,
            HeightRequest = 56,
            Padding = 11,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Content = new Image
            {
                Source = "himo_theme_p_neon_violet_himo_s7_notif_group.png",
                Aspect = Aspect.AspectFit
            }
        };
        icon.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
        icon.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        content.Add(icon, 0, 0);

        var labels = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        labels.Add(MakeLabel("مجموعة جديدة", 16, true, "HimoPrimary"));
        labels.Add(MakeLabel($"{_draft.Members.Count} أعضاء", 12, false, "HimoTextSecondary"));
        content.Add(labels, 1, 0);

        var card = MakeCard(content);
        card.SetDynamicResource(Border.StrokeProperty, "HimoAccent");
        return card;
    }

    private View CreateMembersStrip()
    {
        var members = new HorizontalStackLayout { Spacing = 12, Padding = new Thickness(2, 4) };
        foreach (var member in _draft.Members)
        {
            var initial = MakeLabel(
                string.IsNullOrWhiteSpace(member.Name) ? "؟" : member.Name.Trim()[0].ToString(),
                16, true, "HimoAccent");
            initial.HorizontalTextAlignment = TextAlignment.Center;
            initial.VerticalTextAlignment = TextAlignment.Center;
            var hasPhoto = !string.IsNullOrWhiteSpace(member.PhotoPath) && File.Exists(member.PhotoPath);
            var photo = new Image
            {
                Aspect = Aspect.AspectFill,
                IsVisible = hasPhoto,
                Source = hasPhoto ? ImageSource.FromFile(member.PhotoPath!) : null
            };
            initial.IsVisible = !hasPhoto;
            var avatarContent = new Grid();
            avatarContent.Add(initial);
            avatarContent.Add(photo);
            if (member.UserId != Guid.Empty)
                _memberAvatarViews[member.UserId] = (photo, initial);
            var avatar = new Border
            {
                WidthRequest = 42,
                HeightRequest = 42,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(21) },
                Clip = new EllipseGeometry { Center = new Point(21, 21), RadiusX = 21, RadiusY = 21 },
                Content = avatarContent,
                HorizontalOptions = LayoutOptions.Center
            };
            avatar.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
            avatar.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
            var name = MakeLabel(member.Name, 10.5, true, "HimoPrimary");
            name.WidthRequest = 66;
            name.HorizontalTextAlignment = TextAlignment.Center;
            name.LineBreakMode = LineBreakMode.TailTruncation;
            members.Add(new VerticalStackLayout
            {
                Spacing = 5,
                WidthRequest = 68,
                Children = { avatar, name }
            });
        }
        return new ScrollView
        {
            Content = members,
            Orientation = ScrollOrientation.Horizontal,
            HeightRequest = 78,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never
        };
    }

    private View MakeSectionTitle(string text)
    {
        var label = MakeLabel(text, 13, true, "HimoPrimary");
        label.Margin = new Thickness(2, 2, 2, -5);
        return label;
    }

    private Border CreateFieldCard(View field, double height)
    {
        var content = new Grid { Padding = new Thickness(13, 3), HeightRequest = height };
        content.Add(field, 0, 0);
        return MakeCard(content);
    }

    private async Task OpenPermissionsAsync()
    {
        await Navigation.PushModalAsync(new NavigationPage(new GroupPermissionsPage(_draft)));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdatePermissionSummary();
        _ = LoadMemberPhotosAsync();
    }

    private void UpdatePermissionSummary()
    {
        if (_permissionSummary is null) return;
        var add = _draft.AllMembersCanAddMembers ? "الجميع" : "المشرف فقط";
        var messages = _draft.AdminsOnlyMessages ? "المشرف فقط" : "الجميع";
        _permissionSummary.Text = $"إضافة الأعضاء: {add}  ·  الرسائل: {messages}";
    }

    private async void CreateGroupClicked(object? sender, EventArgs e)
    {
        if (_creating) return;
        var name = _nameEntry.Text?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 60)
        {
            await DisplayAlertAsync("اسم المجموعة", "اكتب اسمًا من حرفين إلى 60 حرفًا.", "حسنًا");
            return;
        }
        if (_draft.Members.Count == 0)
        {
            await DisplayAlertAsync("أعضاء المجموعة", "اختر عضوًا واحدًا على الأقل.", "حسنًا");
            return;
        }

        try
        {
            _creating = true;
            _draft.Name = name;
            _createButton.IsEnabled = false;
            _busy.IsVisible = _busy.IsRunning = true;
            var group = await _api.CreateGroupAsync(
                name,
                _draft.Members.Select(x => x.UserId).Distinct().ToArray(),
                _draft.DisappearingSeconds,
                _draft.AllMembersCanAddMembers,
                _draft.AllMembersCanShareInviteLink,
                _draft.RequireAdminApproval,
                _draft.AdminsOnlyMessages);

            try { await _home.RefreshFromServerAsync(); }
            catch (Exception refreshError) { System.Diagnostics.Debug.WriteLine($"[Himo Groups] Refresh after creation failed: {refreshError}"); }

            if (Shell.Current is not null)
                await Shell.Current.GoToAsync($"chat?id={group.Id:D}", false);
            else
                await DisplayAlertAsync("تم إنشاء المجموعة", "تم إنشاء المجموعة بنجاح.", "حسنًا");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Groups] Create group failed: {ex}");
            await DisplayAlertAsync("تعذر إنشاء المجموعة", ex.Message, "حسنًا");
        }
        finally
        {
            _creating = false;
            _createButton.IsEnabled = true;
            _busy.IsVisible = _busy.IsRunning = false;
        }
    }

    private static Border MakeCard(View content)
    {
        var card = new Border
        {
            Content = content,
            Padding = new Thickness(0),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) }
        };
        card.SetDynamicResource(BackgroundColorProperty, "HimoSurface");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return card;
    }

    private static Label MakeLabel(string text, double size, bool bold, string colorKey)
    {
        var label = new Label
        {
            Text = text,
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        label.SetDynamicResource(Label.TextColorProperty, colorKey);
        return label;
    }

    private static Button MakeBackButton()
    {
        var button = new Button
        {
            Text = "‹",
            WidthRequest = 42,
            HeightRequest = 42,
            CornerRadius = 21,
            Padding = 0,
            FontSize = 25,
            BorderWidth = 1
        };
        button.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
        button.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        return button;
    }

    private static Button MakePrimaryButton(string text, double height)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 15,
            HeightRequest = height,
            CornerRadius = 17,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(16, 0)
        };
        button.SetDynamicResource(BackgroundColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        return button;
    }
}

/// <summary>Group-level privacy and messaging permissions.</summary>
public sealed class GroupPermissionsPage : ContentPage
{
    private readonly GroupDraft _draft;
    private Label _addMembersValue = new();
    private Label _shareLinkValue = new();
    private Label _sendMessagesValue = new();
    private Switch _approvalSwitch = new();

    public GroupPermissionsPage(GroupDraft draft)
    {
        _draft = draft;
        Title = "أذونات المجموعة";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid
        {
            Padding = new Thickness(18, 12, 18, 14),
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) },
            RowSpacing = 12
        };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var body = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(0, 0, 0, 10) };
        body.Add(CreateHeader());
        body.Add(MakeSectionTitle("الانضمام وإدارة الأعضاء"));

        var memberPermissions = new VerticalStackLayout { Spacing = 0 };
        _addMembersValue = MakeValueLabel();
        memberPermissions.Add(MakePermissionRow("إضافة أعضاء جدد", _addMembersValue, async () =>
        {
            await Navigation.PushAsync(new GroupPermissionChoicePage(
                "إضافة أعضاء جدد", "المشرف فقط", "الجميع", _draft.AllMembersCanAddMembers,
                allowAll => _draft.AllMembersCanAddMembers = allowAll));
        }));
        memberPermissions.Add(CreateDivider());

        _shareLinkValue = MakeValueLabel();
        memberPermissions.Add(MakePermissionRow("مشاركة رابط الدعوة", _shareLinkValue, async () =>
        {
            await Navigation.PushAsync(new GroupPermissionChoicePage(
                "مشاركة رابط الدعوة", "المشرفون فقط", "الجميع", _draft.AllMembersCanShareInviteLink,
                allowAll => _draft.AllMembersCanShareInviteLink = allowAll));
        }));
        memberPermissions.Add(CreateDivider());

        var approvalRow = new Grid
        {
            Padding = new Thickness(15, 12),
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(58) },
            ColumnSpacing = 10,
            MinimumHeightRequest = 68
        };
        approvalRow.Add(MakeLabel("يتطلب موافقة المشرف", 14, true, "HimoPrimary"), 0, 0);
        _approvalSwitch = new Switch
        {
            IsToggled = _draft.RequireAdminApproval,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        _approvalSwitch.SetDynamicResource(Switch.OnColorProperty, "HimoAccent");
        _approvalSwitch.Toggled += (_, e) => _draft.RequireAdminApproval = e.Value;
        approvalRow.Add(_approvalSwitch, 1, 0);
        memberPermissions.Add(approvalRow);
        body.Add(MakeCard(memberPermissions));

        body.Add(MakeSectionTitle("المراسلات"));
        var messagePermissions = new VerticalStackLayout { Spacing = 0 };
        _sendMessagesValue = MakeValueLabel();
        messagePermissions.Add(MakePermissionRow("إرسال الرسائل", _sendMessagesValue, async () =>
        {
            await Navigation.PushAsync(new GroupPermissionChoicePage(
                "إرسال الرسائل", "الجميع", "المشرفون فقط", _draft.AdminsOnlyMessages,
                adminsOnly => _draft.AdminsOnlyMessages = adminsOnly));
        }));
        body.Add(MakeCard(messagePermissions));

        root.Add(new ScrollView
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        }, 0, 0);
        var save = MakePrimaryButton("حفظ", 52);
        save.Clicked += async (_, _) => await Navigation.PopModalAsync();
        root.Add(save, 0, 1);
        Content = root;
        UpdateValues();
    }

    private View CreateHeader()
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(46), new(GridLength.Star), new(46) },
            HeightRequest = 50
        };
        var back = MakeBackButton();
        back.Clicked += async (_, _) => await Navigation.PopModalAsync();
        header.Add(back, 0, 0);
        var title = MakeLabel("أذونات المجموعة", 20, true, "HimoPrimary");
        title.HorizontalTextAlignment = TextAlignment.Center;
        header.Add(title, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0);
        return header;
    }

    private View MakeSectionTitle(string text)
    {
        var label = MakeLabel(text, 13, true, "HimoPrimary");
        label.Margin = new Thickness(2, 1, 2, -4);
        return label;
    }

    private View MakePermissionRow(string title, Label value, Func<Task> action)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(26) },
            Padding = new Thickness(15, 12),
            ColumnSpacing = 8,
            MinimumHeightRequest = 68
        };
        var labels = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        labels.Add(MakeLabel(title, 14, true, "HimoPrimary"));
        labels.Add(value);
        row.Add(labels, 0, 0);
        var arrow = MakeLabel("‹", 24, true, "HimoAccent");
        arrow.HorizontalTextAlignment = TextAlignment.Center;
        row.Add(arrow, 1, 0);
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await action()) });
        return row;
    }

    private void UpdateValues()
    {
        _addMembersValue.Text = _draft.AllMembersCanAddMembers ? "الجميع" : "المشرف فقط";
        _shareLinkValue.Text = _draft.AllMembersCanShareInviteLink ? "الجميع" : "المشرفون فقط";
        _sendMessagesValue.Text = _draft.AdminsOnlyMessages ? "المشرفون فقط" : "الجميع";
        if (_approvalSwitch is not null) _approvalSwitch.IsToggled = _draft.RequireAdminApproval;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateValues();
    }

    private static Label MakeValueLabel()
    {
        var label = MakeLabel(string.Empty, 11.5, true, "HimoAccent");
        return label;
    }

    private static BoxView CreateDivider()
    {
        var divider = new BoxView { HeightRequest = 1, Margin = new Thickness(14, 0), Opacity = 0.7 };
        divider.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoDivider");
        return divider;
    }

    private static Border MakeCard(View content)
    {
        var card = new Border
        {
            Content = content,
            Padding = new Thickness(0),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) }
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSurface");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return card;
    }

    private static Label MakeLabel(string text, double size, bool bold, string colorKey)
    {
        var label = new Label
        {
            Text = text,
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            VerticalTextAlignment = TextAlignment.Center
        };
        label.SetDynamicResource(Label.TextColorProperty, colorKey);
        return label;
    }

    private static Button MakeBackButton()
    {
        var button = new Button { Text = "‹", WidthRequest = 42, HeightRequest = 42, CornerRadius = 21, Padding = 0, FontSize = 25, BorderWidth = 1 };
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        button.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        return button;
    }

    private static Button MakePrimaryButton(string text, double height)
    {
        var button = new Button { Text = text, HeightRequest = height, CornerRadius = 17, FontSize = 15, FontAttributes = FontAttributes.Bold };
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        return button;
    }
}

/// <summary>Radio-style selection page for a single group permission.</summary>
public sealed class GroupPermissionChoicePage : ContentPage
{
    private readonly Action<bool> _apply;
    private readonly string _firstOption;
    private readonly string _secondOption;
    private bool _secondSelected;
    private Border _firstCard = new();
    private Border _secondCard = new();
    private Label _firstCheck = new();
    private Label _secondCheck = new();

    public GroupPermissionChoicePage(string title, string firstOption, string secondOption, bool secondSelected, Action<bool> apply)
    {
        _apply = apply;
        _firstOption = firstOption;
        _secondOption = secondOption;
        _secondSelected = secondSelected;
        Title = title;
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid
        {
            Padding = new Thickness(18, 12, 18, 14),
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) },
            RowSpacing = 12
        };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var body = new VerticalStackLayout { Spacing = 14 };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(46), new(GridLength.Star), new(46) },
            HeightRequest = 50
        };
        var back = MakeBackButton();
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        var title = MakeLabel(Title, 19, true, "HimoPrimary");
        title.HorizontalTextAlignment = TextAlignment.Center;
        header.Add(title, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0);
        body.Add(header);

        _firstCard = BuildChoiceCard(_firstOption, false, out _firstCheck);
        _secondCard = BuildChoiceCard(_secondOption, true, out _secondCheck);
        _firstCard.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectChoice(false)) });
        _secondCard.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectChoice(true)) });
        body.Add(_firstCard);
        body.Add(_secondCard);

        root.Add(new ScrollView { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 0, 0);
        var save = MakePrimaryButton("حفظ", 52);
        save.Clicked += async (_, _) =>
        {
            _apply(_secondSelected);
            await Navigation.PopAsync();
        };
        root.Add(save, 0, 1);
        Content = root;
        UpdateChoiceVisuals();
    }

    private Border BuildChoiceCard(string option, bool isSecond, out Label check)
    {
        var content = new Grid
        {
            Padding = new Thickness(16, 17),
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(34) },
            ColumnSpacing = 12,
            MinimumHeightRequest = 70
        };
        content.Add(MakeLabel(option, 15, true, "HimoPrimary"), 0, 0);
        check = new Label
        {
            Text = "✓",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        var checkCircle = new Border
        {
            WidthRequest = 26,
            HeightRequest = 26,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
            Content = check,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };
        checkCircle.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
        checkCircle.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        content.Add(checkCircle, 1, 0);
        var card = MakeCard(content);
        card.ClassId = isSecond ? "second" : "first";
        return card;
    }

    private void SelectChoice(bool second)
    {
        _secondSelected = second;
        UpdateChoiceVisuals();
    }

    private void UpdateChoiceVisuals()
    {
        ApplyChoiceVisual(_firstCard, _firstCheck, !_secondSelected);
        ApplyChoiceVisual(_secondCard, _secondCheck, _secondSelected);
    }

    private static void ApplyChoiceVisual(Border card, Label check, bool selected)
    {
        card.StrokeThickness = selected ? 1.6 : 1;
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, selected ? "HimoSurfaceElevated" : "HimoSurface");
        card.SetDynamicResource(Border.StrokeProperty, selected ? "HimoAccent" : "HimoBorder");
        check.Text = selected ? "✓" : string.Empty;
        check.SetDynamicResource(Label.TextColorProperty, "HimoOnAccent");
        if (card.Content is Grid grid && grid.Children.Count > 1 && grid.Children[1] is Border circle)
        {
            circle.SetDynamicResource(VisualElement.BackgroundColorProperty, selected ? "HimoAccent" : "HimoSoft");
            circle.SetDynamicResource(Border.StrokeProperty, selected ? "HimoAccent" : "HimoBorder");
        }
    }

    private static Border MakeCard(View content)
    {
        var card = new Border
        {
            Content = content,
            Padding = new Thickness(0),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) }
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSurface");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return card;
    }

    private static Label MakeLabel(string text, double size, bool bold, string key)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, VerticalTextAlignment = TextAlignment.Center };
        label.SetDynamicResource(Label.TextColorProperty, key);
        return label;
    }

    private static Button MakeBackButton()
    {
        var button = new Button { Text = "‹", WidthRequest = 42, HeightRequest = 42, CornerRadius = 21, Padding = 0, FontSize = 25, BorderWidth = 1 };
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        button.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        return button;
    }

    private static Button MakePrimaryButton(string text, double height)
    {
        var button = new Button { Text = text, HeightRequest = height, CornerRadius = 17, FontSize = 15, FontAttributes = FontAttributes.Bold };
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        return button;
    }
}
