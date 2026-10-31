using Himo.Models;
using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>Dedicated group profile page. Group profile data and actions are server-backed.</summary>
public sealed class GroupConversationInfoPage : ContentPage
{
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Conversation _conversation;
    private readonly Guid _groupId;
    private readonly Image _avatar = new() { Aspect = Aspect.AspectFill };
    private readonly Label _initial = new() { FontSize = 42, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
    private readonly Label _name = new() { FontSize = 23, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _memberCount = new() { FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _description = new() { FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap };
    private Button? _editPhotoButton;
    private readonly ActivityIndicator _busy = new() { IsVisible = false, IsRunning = false };
    private HimoApiClient.GroupDetailsDto? _details;
    private string? _groupPhotoPath;
    private bool _working;
    private bool _detailsLoaded;
    private bool _reloadDetailsWhenAppearing;
    private bool _loadingGroup;

    public GroupConversationInfoPage(ChatService chat, HimoApiClient api, AccountService account, Conversation conversation)
    {
        _chat = chat;
        _api = api;
        _account = account;
        _conversation = conversation;
        _groupId = Guid.TryParse(conversation.RemoteId, out var parsedGroupId) ? parsedGroupId : Guid.Empty;
        Title = "معلومات المجموعة";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) },
            BackgroundColor = Colors.Transparent
        };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            Padding = new Thickness(16, 10),
            ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star), new(44) },
            ColumnSpacing = 8
        };
        var back = MakeIconButton("‹");
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        var title = MakeLabel("معلومات المجموعة", 18, true);
        title.HorizontalTextAlignment = TextAlignment.Center;
        header.Add(title, 1, 0);
        var more = MakeIconButton("⋮");
        more.Clicked += MoreClicked;
        header.Add(more, 2, 0);
        root.Add(header, 0, 0);

        var body = new VerticalStackLayout { Padding = new Thickness(18, 8, 18, 28), Spacing = 16 };
        var profile = new VerticalStackLayout { Spacing = 9, HorizontalOptions = LayoutOptions.Fill };
        var avatarArea = new Grid { WidthRequest = 132, HeightRequest = 132, HorizontalOptions = LayoutOptions.Center };
        var avatarBorder = new Border
        {
            WidthRequest = 124, HeightRequest = 124, Padding = 0, StrokeThickness = 2,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Ellipse(), Content = new Grid()
        };
        avatarBorder.SetDynamicResource(Border.BackgroundColorProperty, "HimoCard");
        avatarBorder.SetDynamicResource(Border.StrokeProperty, "HimoAccent");
        _avatar.WidthRequest = 120; _avatar.HeightRequest = 120;
        _avatar.Clip = new EllipseGeometry { Center = new Point(60, 60), RadiusX = 60, RadiusY = 60 };
        _initial.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        ((Grid)avatarBorder.Content).Add(_initial);
        ((Grid)avatarBorder.Content).Add(_avatar);
        avatarArea.Add(avatarBorder);
        _editPhotoButton = MakeIconButton("✎");
        _editPhotoButton.WidthRequest = 39; _editPhotoButton.HeightRequest = 39; _editPhotoButton.FontSize = 17;
        _editPhotoButton.HorizontalOptions = LayoutOptions.End; _editPhotoButton.VerticalOptions = LayoutOptions.End;
        _editPhotoButton.IsVisible = false;
        _editPhotoButton.Clicked += ChangePhotoClicked;
        avatarArea.Add(_editPhotoButton);
        var photoTap = new TapGestureRecognizer();
        photoTap.Tapped += async (_, _) => await OpenGroupPhotoAsync();
        avatarBorder.GestureRecognizers.Add(photoTap);
        profile.Add(avatarArea);

        _name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        profile.Add(_name);
        _memberCount.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        profile.Add(_memberCount);
        _description.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        var descriptionTap = new TapGestureRecognizer();
        descriptionTap.Tapped += async (_, _) => await EditDescriptionAsync();
        _description.GestureRecognizers.Add(descriptionTap);
        profile.Add(_description);
        body.Add(profile);


        body.Add(BuildSectionRow("الأعضاء", "♙", async () =>
        {
            _reloadDetailsWhenAppearing = true;
            await Navigation.PushAsync(new GroupMembersPage(_chat, _api, _account, _conversation, _details));
        }));
        body.Add(BuildSectionRow("الوسائط", "▧", async () =>
            await Navigation.PushAsync(new ConversationSharedContentPage(_chat, _api, _account, _conversation, "media", lightweightGroupMode: true))));
        body.Add(BuildSectionRow("الإعدادات", "⚙", async () =>
            await Navigation.PushAsync(new GroupNotificationSettingsPage(_api, _conversation))));

        _busy.Color = Color.FromArgb("#B16CF0");
        _busy.HorizontalOptions = LayoutOptions.Center;
        body.Add(_busy);

        root.Add(new ScrollView { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 0, 1);
        Content = root;
    }

    private View BuildSectionRow(string title, string glyph, Func<Task> navigate)
    {
        var grid = new Grid
        {
            Padding = new Thickness(14, 13),
            ColumnDefinitions = new ColumnDefinitionCollection { new(48), new(GridLength.Star), new(22) },
            ColumnSpacing = 12
        };
        var icon = new Border
        {
            WidthRequest = 44, HeightRequest = 44, StrokeThickness = 1, Padding = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = MakeLabel(glyph, 21, true)
        };
        icon.SetDynamicResource(Border.BackgroundColorProperty, "HimoSoft");
        icon.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        ((Label)icon.Content).HorizontalTextAlignment = TextAlignment.Center;
        grid.Add(icon, 0, 0);
        grid.Add(MakeLabel(title, 16, true), 1, 0);
        grid.Add(MakeLabel("‹", 25, true), 2, 0);
        var card = ConversationInfoPage.MakeCard(grid);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await navigate();
        card.GestureRecognizers.Add(tap);
        return card;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_detailsLoaded && !_reloadDetailsWhenAppearing) return;
        _reloadDetailsWhenAppearing = false;
        await LoadGroupAsync();
    }

    private async Task LoadGroupAsync()
    {
        if (_loadingGroup || _groupId == Guid.Empty || !_api.HasToken) return;
        _loadingGroup = true;
        _busy.IsVisible = _busy.IsRunning = true;
        try
        {
            var details = await _api.GetGroupDetailsAsync(_groupId);
            _details = details;
            _detailsLoaded = true;
            _name.Text = details.Name;
            _conversation.Name = details.Name;
            _memberCount.Text = $"مجموعة · {details.Members.Count} أعضاء";
            _description.Text = string.IsNullOrWhiteSpace(details.Description) ? "إضافة وصف للمجموعة" : details.Description;
            if (_editPhotoButton is not null) _editPhotoButton.IsVisible = details.CanManage;

            // Keep the page interactive: download the group image after the core
            // group details are rendered instead of holding the navigation transition.
            if (details.HasPhoto)
                _ = LoadGroupPhotoInBackgroundAsync(details.HasPhoto, details.PhotoVersion);
            else
                await LoadGroupPhotoAsync(false, details.PhotoVersion);
        }
        catch (Exception ex)
        {
            _detailsLoaded = false;
            await DisplayAlertAsync("معلومات المجموعة", ex.Message, "حسنًا");
        }
        finally
        {
            _busy.IsRunning = _busy.IsVisible = false;
            _loadingGroup = false;
        }
    }

    private async Task LoadGroupPhotoInBackgroundAsync(bool hasPhoto, long version)
    {
        try { await LoadGroupPhotoAsync(hasPhoto, version); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Group] Photo load: {ex.Message}"); }
    }

    private async Task LoadGroupPhotoAsync(bool hasPhoto, long version)
    {
        if (!hasPhoto)
        {
            _avatar.Source = null; _avatar.IsVisible = false; _initial.IsVisible = true;
            _initial.Text = string.IsNullOrWhiteSpace(_name.Text) ? "م" : _name.Text.Trim()[0].ToString();
            _groupPhotoPath = null; _conversation.ProfilePhotoPath = null;
            return;
        }
        var path = FindGroupPhoto(_groupId, version);
        if (path is null)
        {
            var photo = await _api.GetGroupPhotoAsync(_groupId);
            if (photo is null) return;
            var ext = photo.ContentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" :
                photo.ContentType.Contains("webp", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".jpg";
            path = global::System.IO.Path.Combine(FileSystem.Current.CacheDirectory, $"himo_group_{_groupId:N}_v{version}{ext}");
            await File.WriteAllBytesAsync(path, photo.Bytes);
        }
        _groupPhotoPath = path;
        _conversation.ProfilePhotoPath = path;
        _avatar.Source = ImageSource.FromFile(path);
        _avatar.IsVisible = true; _initial.IsVisible = false;
    }

    private static string? FindGroupPhoto(Guid id, long version)
    {
        try
        {
            var pattern = $"himo_group_{id:N}_v{Math.Max(0, version)}.*";
            return Directory.Exists(FileSystem.Current.CacheDirectory)
                ? Directory.EnumerateFiles(FileSystem.Current.CacheDirectory, pattern).FirstOrDefault(File.Exists)
                : null;
        }
        catch { return null; }
    }

    private async void MoreClicked(object? sender, EventArgs e)
    {
        if (_working || _details is null) return;
        var options = new List<string>();
        if (_details.CanManage)
        {
            options.Add("تعديل الاسم");
            options.Add("تعديل الوصف");
            options.Add(_details.HasPhoto ? "تغيير الصورة" : "إضافة صورة");
            if (_details.HasPhoto) options.Add("إزالة الصورة");
        }
        if (_details.CanShareInviteLink) options.Add("مشاركة");
        if (options.Count == 0) return;

        var selected = await DisplayActionSheetAsync("المجموعة", "إلغاء", null, options.ToArray());
        switch (selected)
        {
            case "تعديل الاسم": await EditNameAsync(); break;
            case "تعديل الوصف": await EditDescriptionAsync(); break;
            case "تغيير الصورة":
            case "إضافة صورة": await ChangePhotoAsync(); break;
            case "إزالة الصورة": await DeletePhotoAsync(); break;
            case "مشاركة":
                if (_details is not null)
                {
                    var link = BuildGroupInviteLink(_groupId, _details.InviteCode);
                    await Navigation.PushAsync(new GroupSharePage(link, _conversation.Name));
                }
                break;
        }
    }

    internal static string BuildGroupInviteLink(Guid groupId, string inviteCode) =>
        $"himo://group/{groupId:D}?invite={Uri.EscapeDataString(inviteCode ?? string.Empty)}";

    private async Task EditNameAsync()
    {
        var details = _details;
        if (details?.CanManage != true) return;
        await Navigation.PushAsync(new GroupEditNamePage(details.Name, newName =>
            SaveGroupInfoAsync(newName, details.Description)));
    }

    private async Task EditDescriptionAsync()
    {
        var details = _details;
        if (details?.CanManage != true) return;
        await Navigation.PushAsync(new GroupEditDescriptionPage(details.Description, description =>
            SaveGroupInfoAsync(details.Name, description)));
    }

    private async Task<bool> SaveGroupInfoAsync(string name, string description)
    {
        if (_working) return false;
        _working = true;
        try
        {
            await _api.UpdateGroupInfoAsync(_groupId, name, description);
            _name.Text = name; _conversation.Name = name;
            _details = await _api.GetGroupDetailsAsync(_groupId);
            _memberCount.Text = $"مجموعة · {_details.Members.Count} أعضاء";
            _description.Text = string.IsNullOrWhiteSpace(_details.Description) ? "إضافة وصف للمجموعة" : _details.Description;
            return true;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر حفظ التعديل", ex.Message, "حسنًا");
            return false;
        }
        finally { _working = false; }
    }

    private async void ChangePhotoClicked(object? sender, EventArgs e) => await ChangePhotoAsync();

    private async Task ChangePhotoAsync()
    {
        if (_details?.CanManage != true || _working) return;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "اختر صورة المجموعة", FileTypes = FilePickerFileType.Images });
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            if (memory.Length > 4 * 1024 * 1024) { await DisplayAlertAsync("صورة المجموعة", "الحد الأقصى للصورة 4 ميغابايت.", "حسنًا"); return; }
            var type = file.ContentType;
            if (string.IsNullOrWhiteSpace(type)) type = global::System.IO.Path.GetExtension(file.FileName).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
            _working = true;
            await _api.UploadGroupPhotoAsync(_groupId, memory.ToArray(), type);
            _details = await _api.GetGroupDetailsAsync(_groupId);
            await LoadGroupPhotoAsync(_details.HasPhoto, _details.PhotoVersion);
        }
        catch (Exception ex) { await DisplayAlertAsync("تعذر تغيير الصورة", ex.Message, "حسنًا"); }
        finally { _working = false; }
    }

    private async Task DeletePhotoAsync()
    {
        if (_details?.CanManage != true || !_details.HasPhoto) return;
        if (!await DisplayAlertAsync("إزالة صورة المجموعة", "هل تريد إزالة الصورة الحالية؟", "إزالة", "إلغاء")) return;
        try
        {
            await _api.DeleteGroupPhotoAsync(_groupId);
            _details = await _api.GetGroupDetailsAsync(_groupId);
            await LoadGroupPhotoAsync(false, _details.PhotoVersion);
        }
        catch (Exception ex) { await DisplayAlertAsync("تعذر إزالة الصورة", ex.Message, "حسنًا"); }
    }

    private async Task OpenGroupPhotoAsync()
    {
        if (!string.IsNullOrWhiteSpace(_groupPhotoPath) && File.Exists(_groupPhotoPath))
        {
            await Navigation.PushAsync(new GroupImagePreviewPage(_groupPhotoPath, "صورة المجموعة"));
            return;
        }

        if (_details?.HasPhoto == true)
        {
            await LoadGroupPhotoAsync(true, _details.PhotoVersion);
            if (!string.IsNullOrWhiteSpace(_groupPhotoPath) && File.Exists(_groupPhotoPath))
                await Navigation.PushAsync(new GroupImagePreviewPage(_groupPhotoPath, "صورة المجموعة"));
            return;
        }

        if (_details?.CanManage == true) await ChangePhotoAsync();
    }

    internal static Button MakeBack()
    {
        var button = new Button { Text = "‹", WidthRequest = 42, HeightRequest = 42, Padding = 0, CornerRadius = 14, FontSize = 24 };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoSurface");
        button.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        button.BorderWidth = 1;
        return button;
    }

    private static Label MakeLabel(string text, double size, bool bold, bool secondary = false)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, VerticalTextAlignment = TextAlignment.Center };
        label.SetDynamicResource(Label.TextColorProperty, secondary ? "HimoTextSecondary" : "HimoPrimary");
        return label;
    }

    private static Button MakeIconButton(string text)
    {
        var button = new Button { Text = text, WidthRequest = 42, HeightRequest = 42, Padding = 0, CornerRadius = 14, FontSize = 22 };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoSurface");
        button.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        button.BorderWidth = 1;
        return button;
    }

    private static Button MakePrimaryButton(string text, double height)
    {
        var button = new Button { Text = text, HeightRequest = height, CornerRadius = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        return button;
    }

    private static Button MakeSecondaryButton(string text, double height)
    {
        var button = new Button { Text = text, HeightRequest = height, CornerRadius = 14, FontSize = 13, FontAttributes = FontAttributes.Bold };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoSurface");
        button.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        button.BorderWidth = 1;
        return button;
    }
}

/// <summary>Dedicated group-name editor with an emoji control beside the name field.</summary>
public sealed class GroupEditNamePage : ContentPage
{
    private readonly Entry _nameEntry;
    private readonly Func<string, Task<bool>> _save;
    private bool _saving;

    public GroupEditNamePage(string currentName, Func<string, Task<bool>> save)
    {
        _save = save;
        Title = "اسم المجموعة";
        Shell.SetNavBarIsVisible(this, false);
        FlowDirection = FlowDirection.RightToLeft;

        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var header = new Grid { Padding = new Thickness(16, 10), ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star) } };
        var back = GroupConversationInfoPage.MakeBack();
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        var title = new Label { Text = "اسم المجموعة", FontSize = 19, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        header.Add(title, 1, 0);
        root.Add(header, 0, 0);

        var body = new VerticalStackLayout { Padding = new Thickness(20, 24), Spacing = 12, VerticalOptions = LayoutOptions.Start };
        body.Add(new Label { Text = "الاسم", FontSize = 13, FontAttributes = FontAttributes.Bold }.WithColor("HimoTextSecondary"));
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(54) }, ColumnSpacing = 9 };
        _nameEntry = new Entry { Text = currentName, Placeholder = "اسم المجموعة", MaxLength = 60, HeightRequest = 54, ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
        _nameEntry.SetDynamicResource(Entry.TextColorProperty, "HimoPrimary");
        _nameEntry.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSurface");
        var entryCard = ConversationInfoPage.MakeCard(_nameEntry);
        nameRow.Add(entryCard, 0, 0);
        var emoji = new Button { Text = "☺", FontSize = 25, HeightRequest = 54, CornerRadius = 14 };
        emoji.SetDynamicResource(Button.BackgroundColorProperty, "HimoSurface");
        emoji.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        emoji.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        emoji.BorderWidth = 1;
        emoji.Clicked += ChooseEmojiClicked;
        nameRow.Add(emoji, 1, 0);
        body.Add(nameRow);
        root.Add(new ScrollView { Content = body }, 0, 1);

        var saveButton = new Button { Text = "حفظ", HeightRequest = 50, Margin = new Thickness(20, 10, 20, 22), CornerRadius = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        saveButton.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        saveButton.Clicked += SaveClicked;
        root.Add(saveButton, 0, 2);
        Content = root;
        AppLocalization.AttachToPage(this);
    }

    private async void ChooseEmojiClicked(object? sender, EventArgs e)
    {
        var emoji = await DisplayActionSheetAsync("اختر رمزًا", "إلغاء", null, "❤️", "💕", "🌸", "✨", "⭐", "🎉", "👨‍👩‍👧‍👦", "🤍", "💜", "🌷", "😊", "🎯", "🏡");
        if (string.IsNullOrWhiteSpace(emoji) || emoji == "إلغاء") return;
        var current = (_nameEntry.Text ?? string.Empty).Trim();
        var first = current.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var known = new HashSet<string>(StringComparer.Ordinal) { "❤️", "💕", "🌸", "✨", "⭐", "🎉", "👨‍👩‍👧‍👦", "🤍", "💜", "🌷", "😊", "🎯", "🏡" };
        if (first is not null && known.Contains(first))
            current = current.Length > first.Length ? current[(first.Length + 1)..].TrimStart() : string.Empty;
        _nameEntry.Text = string.IsNullOrWhiteSpace(current) ? emoji : $"{emoji} {current}";
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (_saving) return;
        var name = (_nameEntry.Text ?? string.Empty).Trim();
        if (name.Length < 2)
        {
            await DisplayAlertAsync("اسم المجموعة", "اكتب اسمًا من حرفين على الأقل.", "حسنًا");
            return;
        }
        _saving = true;
        try
        {
            if (await _save(name)) await Navigation.PopAsync();
        }
        catch (Exception ex) { await DisplayAlertAsync("تعذر حفظ الاسم", ex.Message, "حسنًا"); }
        finally { _saving = false; }
    }
}

public sealed class GroupEditDescriptionPage : ContentPage
{
    private readonly Editor _descriptionEntry;
    private readonly Func<string, Task<bool>> _save;
    private bool _saving;

    public GroupEditDescriptionPage(string currentDescription, Func<string, Task<bool>> save)
    {
        _save = save;
        Title = "وصف المجموعة";
        Shell.SetNavBarIsVisible(this, false);
        FlowDirection = FlowDirection.RightToLeft;

        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var header = new Grid { Padding = new Thickness(16, 10), ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star) } };
        var back = GroupConversationInfoPage.MakeBack();
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        var title = new Label { Text = "وصف المجموعة", FontSize = 19, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        header.Add(title, 1, 0);
        root.Add(header, 0, 0);

        var body = new VerticalStackLayout { Padding = new Thickness(20, 24), Spacing = 12, VerticalOptions = LayoutOptions.Start };
        body.Add(new Label { Text = "الوصف", FontSize = 13, FontAttributes = FontAttributes.Bold }.WithColor("HimoTextSecondary"));
        _descriptionEntry = new Editor
        {
            Text = currentDescription,
            Placeholder = "اكتب وصف المجموعة",
            MaxLength = 500,
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 150
        };
        _descriptionEntry.SetDynamicResource(Editor.TextColorProperty, "HimoPrimary");
        _descriptionEntry.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSurface");
        body.Add(ConversationInfoPage.MakeCard(_descriptionEntry));
        root.Add(new ScrollView { Content = body }, 0, 1);

        var saveButton = new Button { Text = "حفظ", HeightRequest = 50, Margin = new Thickness(20, 10, 20, 22), CornerRadius = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        saveButton.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        saveButton.Clicked += SaveClicked;
        root.Add(saveButton, 0, 2);
        Content = root;
        AppLocalization.AttachToPage(this);
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (_saving) return;
        var description = (_descriptionEntry.Text ?? string.Empty).Trim();
        if (description.Length > 500)
        {
            await DisplayAlertAsync("وصف المجموعة", "الحد الأقصى 500 حرف.", "حسنًا");
            return;
        }
        _saving = true;
        try
        {
            if (await _save(description)) await Navigation.PopAsync();
        }
        catch (Exception ex) { await DisplayAlertAsync("تعذر حفظ الوصف", ex.Message, "حسنًا"); }
        finally { _saving = false; }
    }
}

internal static class GroupPageLabelExtensions
{
    public static Label WithColor(this Label label, string resourceKey)
    {
        label.SetDynamicResource(Label.TextColorProperty, resourceKey);
        return label;
    }
}

/// <summary>Virtualized group members list with lazy loading and admin actions.</summary>
public sealed class GroupMembersPage : ContentPage
{
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Conversation _conversation;
    private HimoApiClient.GroupDetailsDto? _details;
    private readonly CollectionView _members = new()
    {
        SelectionMode = SelectionMode.None,
        ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
        VerticalScrollBarVisibility = ScrollBarVisibility.Never
    };
    private readonly ActivityIndicator _busy = new() { IsRunning = false, IsVisible = false };
    private readonly Label _summary = new();
    private Button? _addButton;
    private bool _loaded;
    private bool _loading;
    private bool _needsRefresh;

    public GroupMembersPage(ChatService chat, HimoApiClient api, AccountService account,
        Conversation conversation, HimoApiClient.GroupDetailsDto? initial)
    {
        _chat = chat;
        _api = api;
        _account = account;
        _conversation = conversation;
        _details = initial;
        Title = "الأعضاء";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        BuildPage();
        if (initial is not null)
        {
            _members.ItemsSource = initial.Members;
            _summary.Text = $"{initial.Members.Count} أعضاء";
            _loaded = true;
            _busy.IsVisible = false;
        }
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)
            }
        };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var header = new Grid
        {
            Padding = new Thickness(16, 10),
            ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star), new(44) }
        };
        var back = GroupConversationInfoPage.MakeBack();
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        var title = new Label
        {
            Text = "أعضاء المجموعة", FontSize = 19, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        header.Add(title, 1, 0);
        _addButton = GroupConversationInfoPage.MakeBack();
        _addButton.Text = "+";
        _addButton.FontSize = 25;
        _addButton.IsVisible = _details?.CanManage == true;
        _addButton.Clicked += AddMembersClicked;
        header.Add(_addButton, 2, 0);
        root.Add(header, 0, 0);

        _summary.FontSize = 12;
        _summary.Margin = new Thickness(20, 0, 20, 10);
        _summary.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        root.Add(_summary, 0, 1);

        _members.ItemTemplate = new DataTemplate(CreateMemberRow);
        root.Add(_members, 0, 2);
        var busyOverlay = new Grid
        {
            IsVisible = false, InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
        };
        _busy.Color = Color.FromArgb("#B16CF0");
        busyOverlay.Add(_busy);
        root.Add(busyOverlay, 0, 2);

        // Conversation actions stay below the member list and are available to
        // every participant. Clearing is per-user; leaving removes membership.
        var actions = new VerticalStackLayout
        {
            Padding = new Thickness(16, 8, 16, 18),
            Spacing = 9
        };
        actions.Add(BuildMemberActionRow("⌫", "مسح محتوى الدردشة", false, ClearGroupChatClicked));
        actions.Add(BuildMemberActionRow("↪", "الخروج من المجموعة", true, LeaveGroupClicked));
        root.Add(actions, 0, 3);
        Content = root;
    }

    private Border BuildMemberActionRow(string glyph, string title, bool danger, EventHandler handler)
    {
        var color = danger ? Color.FromArgb("#D95768") : Color.FromArgb("#C27B35");
        var grid = new Grid
        {
            Padding = new Thickness(14, 12),
            ColumnDefinitions = new ColumnDefinitionCollection { new(42), new(GridLength.Star), new(20) },
            ColumnSpacing = 11,
            MinimumHeightRequest = 58
        };
        var icon = new Border
        {
            WidthRequest = 38,
            HeightRequest = 38,
            Padding = 0,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            BackgroundColor = color.WithAlpha(0.13f),
            Content = new Label
            {
                Text = glyph,
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = color,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        grid.Add(icon, 0, 0);
        grid.Add(new Label
        {
            Text = title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = color,
            VerticalTextAlignment = TextAlignment.Center
        }, 1, 0);
        var arrow = new Label { Text = "‹", FontSize = 23, TextColor = color, VerticalTextAlignment = TextAlignment.Center };
        grid.Add(arrow, 2, 0);

        var card = ConversationInfoPage.MakeCard(grid);
        card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => handler(this, EventArgs.Empty)) });
        return card;
    }

    private bool _groupActionBusy;

    private async void ClearGroupChatClicked(object? sender, EventArgs e)
    {
        if (_groupActionBusy) return;
        var confirmed = await DisplayAlertAsync(
            "مسح محتوى الدردشة",
            "سيتم مسح الرسائل من حسابك فقط، ولن تُحذف من حسابات بقية أعضاء المجموعة. هل تريد المتابعة؟",
            "مسح المحتوى", "إلغاء");
        if (!confirmed) return;

        _groupActionBusy = true;
        try
        {
            if (!Guid.TryParse(_conversation.RemoteId, out var groupId) || groupId == Guid.Empty)
                throw new InvalidOperationException("معرّف المجموعة غير صالح.");

            await _api.ClearConversationContentAsync(groupId);
            _chat.ClearConversationMessages(_conversation.Id);
            await DisplayAlertAsync("تم المسح", "تم مسح محتوى الدردشة من حسابك.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر مسح المحتوى", ex.Message, "حسنًا");
        }
        finally { _groupActionBusy = false; }
    }

    private async void LeaveGroupClicked(object? sender, EventArgs e)
    {
        if (_groupActionBusy) return;
        var confirmed = await DisplayAlertAsync(
            "الخروج من المجموعة",
            "بعد الخروج لن تصلك رسائل المجموعة ولن تظهر في قائمة محادثاتك. هل تريد الخروج؟",
            "الخروج", "إلغاء");
        if (!confirmed) return;

        _groupActionBusy = true;
        try
        {
            if (!Guid.TryParse(_conversation.RemoteId, out var groupId) || groupId == Guid.Empty)
                throw new InvalidOperationException("معرّف المجموعة غير صالح.");

            await _api.LeaveGroupAsync(groupId);
            _chat.RemoveConversation(_conversation.Id);

            // The group chat is no longer accessible. Return to the conversations
            // home rather than popping back into the now-inaccessible group chat.
            try
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync("//HomePage", false);
                else
                    await Navigation.PopToRootAsync(false);
            }
            catch (Exception navigationException)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Group] Left group but navigation fallback failed: {navigationException.Message}");
                try { await Navigation.PopToRootAsync(false); } catch { }
            }
        }
        catch (Exception ex)
        {
            _groupActionBusy = false;
            await DisplayAlertAsync("تعذر الخروج من المجموعة", ex.Message, "حسنًا");
        }
    }

    private View CreateMemberRow()
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 10),
            ColumnDefinitions = new ColumnDefinitionCollection { new(54), new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 12,
            MinimumHeightRequest = 76
        };
        var avatar = new Grid { WidthRequest = 52, HeightRequest = 52 };
        var border = new Border
        {
            WidthRequest = 50, HeightRequest = 50, StrokeThickness = 1, Padding = 0,
            StrokeShape = new Ellipse()
        };
        border.SetDynamicResource(Border.BackgroundColorProperty, "HimoSoft");
        border.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var initial = new Label
        {
            FontSize = 18, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
        };
        initial.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        var photo = new Image
        {
            Aspect = Aspect.AspectFill, WidthRequest = 50, HeightRequest = 50, IsVisible = false,
            Clip = new EllipseGeometry { Center = new Point(25, 25), RadiusX = 25, RadiusY = 25 }
        };
        var avatarContent = new Grid();
        avatarContent.Add(initial);
        avatarContent.Add(photo);
        border.Content = avatarContent;
        avatar.Add(border);
        row.Add(avatar, 0, 0);

        var labels = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        var name = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold };
        name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var role = new Label { Text = "مشرف المجموعة", FontSize = 11, IsVisible = false };
        role.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        labels.Add(name);
        labels.Add(role);
        row.Add(labels, 1, 0);
        var chevron = new Label { Text = "‹", FontSize = 24, VerticalTextAlignment = TextAlignment.Center };
        chevron.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        row.Add(chevron, 2, 0);

        row.BindingContextChanged += (_, _) =>
        {
            if (row.BindingContext is not HimoApiClient.GroupMemberDto member) return;
            name.Text = member.Name;
            role.IsVisible = member.IsAdmin;
            initial.Text = string.IsNullOrWhiteSpace(member.Name) ? "؟" : member.Name.Trim()[0].ToString();
            photo.IsVisible = false;
            photo.Source = null;
            initial.IsVisible = true;
            if (member.HasProfilePhoto) _ = LoadMemberAvatarAsync(member, photo, initial, row);
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (row.BindingContext is HimoApiClient.GroupMemberDto member)
                await MemberTappedAsync(member);
        };
        row.GestureRecognizers.Add(tap);
        var divider = new BoxView { HeightRequest = 1, Opacity = 0.12, HorizontalOptions = LayoutOptions.Fill };
        divider.SetDynamicResource(BackgroundColorProperty, "HimoTextSecondary");
        return new VerticalStackLayout { Spacing = 0, Children = { row, divider } };
    }

    private async Task LoadMemberAvatarAsync(HimoApiClient.GroupMemberDto member, Image image, Label initial, View row)
    {
        try
        {
            var path = await ProfilePhotoCache.GetPathAsync(_api, member.Id, member.HasProfilePhoto, member.ProfilePhotoVersion);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (row.BindingContext is not HimoApiClient.GroupMemberDto current || current.Id != member.Id) return;
                image.Source = ImageSource.FromFile(path);
                image.IsVisible = true;
                initial.IsVisible = false;
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Group] Member photo load: {ex.Message}"); }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded && !_needsRefresh) return;
        await RefreshMembersAsync();
    }

    private async void AddMembersClicked(object? sender, EventArgs e)
    {
        if (_details?.CanManage != true || _loading) return;
        _needsRefresh = true;
        await Navigation.PushAsync(new GroupAddMembersPage(
            _chat, _api, _account, _conversation, _details,
            () => _needsRefresh = true));
    }

    private async Task RefreshMembersAsync()
    {
        if (_loading) return;
        _loading = true;
        _busy.IsVisible = _busy.IsRunning = true;
        try
        {
            _details = await _api.GetGroupDetailsAsync(Guid.Parse(_conversation.RemoteId!));
            _members.ItemsSource = _details.Members;
            _summary.Text = $"{_details.Members.Count} أعضاء";
            if (_addButton is not null) _addButton.IsVisible = _details.CanManage;
            _loaded = true;
            _needsRefresh = false;
        }
        catch (Exception ex) { await DisplayAlertAsync("الأعضاء", ex.Message, "حسنًا"); }
        finally
        {
            _busy.IsVisible = _busy.IsRunning = false;
            _loading = false;
        }
    }

    private async Task MemberTappedAsync(HimoApiClient.GroupMemberDto member)
    {
        var currentId = _account.CurrentAccount?.UserId ?? Guid.Empty;
        var isSelf = currentId != Guid.Empty && member.Id == currentId;
        if (_details?.CanManage == true && !isSelf && member.Id != Guid.Empty)
        {
            var action = await DisplayActionSheetAsync(member.Name, "إلغاء", null,
                "مراسلة " + member.Name,
                member.IsAdmin ? "إلغاء تعيينه كمشرف" : "تعيين كمشرف",
                "إزالة " + member.Name);
            if (action == "مراسلة " + member.Name)
                await OpenDirectAsync(member, null);
            else if (action == "تعيين كمشرف" || action == "إلغاء تعيينه كمشرف")
            {
                try
                {
                    await _api.SetGroupMemberAdminAsync(Guid.Parse(_conversation.RemoteId!), member.Id, !member.IsAdmin);
                    _needsRefresh = true;
                    await RefreshMembersAsync();
                }
                catch (Exception ex) { await DisplayAlertAsync("إدارة المشرفين", ex.Message, "حسنًا"); }
            }
            else if (action == "إزالة " + member.Name)
            {
                if (!await DisplayAlertAsync("إزالة عضو", $"هل تريد إزالة {member.Name} من المجموعة؟", "إزالة", "إلغاء")) return;
                try
                {
                    await _api.RemoveGroupMemberAsync(Guid.Parse(_conversation.RemoteId!), member.Id);
                    _needsRefresh = true;
                    await RefreshMembersAsync();
                }
                catch (Exception ex) { await DisplayAlertAsync("إزالة عضو", ex.Message, "حسنًا"); }
            }
            return;
        }
        await ShowProfileSheetAsync(member);
    }

    private async Task ShowProfileSheetAsync(HimoApiClient.GroupMemberDto member)
    {
        // Show the sheet immediately; photo download is deferred and must not delay navigation.
        var sheet = new GroupMemberProfileSheetPage(member, null,
            async () => { await Navigation.PopModalAsync(); await OpenDirectAsync(member, null); },
            async mode => { await Navigation.PopModalAsync(); await OpenDirectAsync(member, mode); },
            async () =>
            {
                await Navigation.PopModalAsync();
                var path = await ProfilePhotoCache.GetPathAsync(_api, member.Id, member.HasProfilePhoto, member.ProfilePhotoVersion);
                await Navigation.PushAsync(new GroupMemberInfoPage(member, path));
            },
            async () =>
            {
                var path = await ProfilePhotoCache.GetPathAsync(_api, member.Id, member.HasProfilePhoto, member.ProfilePhotoVersion);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    await Navigation.PushModalAsync(new GroupImagePreviewPage(path, member.Name));
            });
        await Navigation.PushModalAsync(sheet);
        if (member.HasProfilePhoto)
        {
            _ = Task.Run(async () =>
            {
                var path = await ProfilePhotoCache.GetPathAsync(_api, member.Id, member.HasProfilePhoto, member.ProfilePhotoVersion);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    await MainThread.InvokeOnMainThreadAsync(() => sheet.SetPhoto(path));
            });
        }
    }

    private async Task OpenDirectAsync(HimoApiClient.GroupMemberDto member, string? callMode)
    {
        try
        {
            var direct = await _api.CreateConversationAsync(member.Name, member.Id);
            var id = direct.Id.ToString("D");
            if (Shell.Current is not null)
            {
                var route = callMode is null
                    ? $"chat?id={Uri.EscapeDataString(id)}"
                    : $"CallPage?id={Uri.EscapeDataString(id)}&mode={callMode}";
                await Shell.Current.GoToAsync(route, false);
            }
        }
        catch (Exception ex) { await DisplayAlertAsync("تعذر فتح المحادثة", ex.Message, "حسنًا"); }
    }
}

/// <summary>Choose group members from existing direct conversations or Himo contacts.</summary>
public sealed class GroupAddMembersPage : ContentPage
{
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Action _markParentForRefresh;
    private readonly Guid _groupId;
    private readonly HashSet<Guid> _existingIds;
    private List<AddMemberPerson> _items = new();
    private readonly Dictionary<Guid, AddMemberPerson> _selected = new();
    private readonly List<AddMemberPerson> _conversationPeople = new();
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.None, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
    private readonly SearchBar _search = new() { Placeholder = "ابحث بالاسم أو الرقم أو البريد", CancelButtonColor = Color.FromArgb("#B16CF0") };
    private readonly Label _empty = new() { HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, FontSize = 13 };
    private readonly Label _selectedCount = new() { FontSize = 12, VerticalTextAlignment = TextAlignment.Center };
    private readonly Button _addButton = new() { Text = "إضافة", HeightRequest = 48, CornerRadius = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, IsEnabled = false };
    private readonly ActivityIndicator _busy = new() { IsRunning = false, IsVisible = false };
    private Button? _conversationsTab;
    private Button? _contactsTab;
    private CancellationTokenSource? _searchCts;
    private bool _contactsActive;
    private bool _submitting;
    private int _searchVersion;

    public GroupAddMembersPage(ChatService chat, HimoApiClient api, AccountService account,
        Conversation conversation, HimoApiClient.GroupDetailsDto details, Action markParentForRefresh)
    {
        _chat = chat;
        _api = api;
        _account = account;
        _markParentForRefresh = markParentForRefresh;
        _groupId = Guid.Parse(conversation.RemoteId!);
        _existingIds = details.Members.Select(x => x.Id).ToHashSet();
        Title = "إضافة أعضاء";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        BuildPage();
        BuildConversationItems();
        ShowConversationItems();
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)
            }
        };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var header = new Grid { Padding = new Thickness(16, 10), ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star), new(44) } };
        var back = GroupConversationInfoPage.MakeBack();
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        var title = new Label { Text = "إضافة أعضاء", FontSize = 19, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        header.Add(title, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0);
        root.Add(header, 0, 0);

        var tabs = new Grid { Padding = new Thickness(16, 6), ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
        _conversationsTab = TabButton("المحادثات", active: true);
        _contactsTab = TabButton("جهات الاتصال", active: false);
        _conversationsTab.Clicked += (_, _) => SwitchTab(false);
        _contactsTab.Clicked += async (_, _) => await SwitchTabAsync(true);
        tabs.Add(_conversationsTab, 0, 0);
        tabs.Add(_contactsTab, 1, 0);
        root.Add(tabs, 0, 1);

        _search.Margin = new Thickness(16, 4, 16, 8);
        _search.TextChanged += SearchTextChanged;
        _search.SearchButtonPressed += async (_, _) => await SearchContactsAsync(_search.Text, CancellationToken.None);
        _search.SetDynamicResource(SearchBar.TextColorProperty, "HimoPrimary");
        _search.SetDynamicResource(SearchBar.PlaceholderColorProperty, "HimoTextSecondary");
        root.Add(ConversationInfoPage.MakeCard(_search), 0, 2);

        _empty.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        var listArea = new Grid();
        _list.ItemTemplate = new DataTemplate(CreateSelectablePersonRow);
        _list.ItemsSource = _items;
        listArea.Add(_list);
        var overlay = new Grid { InputTransparent = true, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _busy.Color = Color.FromArgb("#B16CF0");
        overlay.Add(_busy);
        listArea.Add(overlay);
        var emptyOverlay = new Grid { InputTransparent = true, Padding = new Thickness(28) };
        emptyOverlay.Add(_empty);
        listArea.Add(emptyOverlay);
        root.Add(listArea, 0, 3);

        var footer = new Grid { Padding = new Thickness(16, 10, 16, 18), ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(130) }, ColumnSpacing = 12 };
        _selectedCount.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        _selectedCount.Text = "لم يتم اختيار أعضاء";
        footer.Add(_selectedCount, 0, 0);
        _addButton.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        _addButton.Clicked += AddSelectedClicked;
        footer.Add(_addButton, 1, 0);
        root.Add(footer, 0, 4);

        Content = root;
    }

    private Button TabButton(string text, bool active)
    {
        var button = new Button { Text = text, HeightRequest = 43, CornerRadius = 14, FontSize = 13, FontAttributes = FontAttributes.Bold };
        ApplyTabStyle(button, active);
        return button;
    }

    private static void ApplyTabStyle(Button button, bool active)
    {
        button.BackgroundColor = active ? Color.FromArgb("#B16CF0") : Colors.Transparent;
        button.TextColor = active ? Colors.White : Color.FromArgb("#B16CF0");
        button.BorderColor = Color.FromArgb("#5B4678");
        button.BorderWidth = active ? 0 : 1;
    }

    private void BuildConversationItems()
    {
        var self = _account.CurrentAccount?.UserId ?? Guid.Empty;
        foreach (var item in _chat.Conversations.Where(x => !x.IsGroup && x.OtherParticipantUserId.HasValue)
                     .GroupBy(x => x.OtherParticipantUserId!.Value)
                     .Select(g => g.OrderByDescending(x => x.UpdatedAt).First())
                     .Where(x => !_existingIds.Contains(x.OtherParticipantUserId!.Value) && x.OtherParticipantUserId != self)
                     .OrderByDescending(x => x.UpdatedAt))
        {
            _conversationPeople.Add(new AddMemberPerson
            {
                Id = item.OtherParticipantUserId!.Value,
                Name = item.Name,
                Subtitle = item.LastMessage,
                PhotoPath = item.ProfilePhotoPath,
                HasPhoto = item.HasRemoteProfilePhoto,
                PhotoVersion = item.ProfilePhotoVersion
            });
        }
    }

    private void SwitchTab(bool contacts)
    {
        Interlocked.Increment(ref _searchVersion);
        _contactsActive = contacts;
        ApplyTabStyle(_conversationsTab!, !contacts);
        ApplyTabStyle(_contactsTab!, contacts);
        _searchCts?.Cancel();
        if (contacts)
        {
            _items = new List<AddMemberPerson>();
            _list.ItemsSource = _items;
            _empty.Text = "ابحث عن اسم أو رقم أو بريد إلكتروني";
            _empty.IsVisible = true;
            if (!string.IsNullOrWhiteSpace(_search.Text)) _ = SearchContactsAsync(_search.Text, CancellationToken.None);
        }
        else ShowConversationItems();
    }

    private Task SwitchTabAsync(bool contacts)
    {
        SwitchTab(contacts);
        return Task.CompletedTask;
    }

    private void ShowConversationItems()
    {
        _contactsActive = false;
        ApplyTabStyle(_conversationsTab!, true);
        ApplyTabStyle(_contactsTab!, false);
        var query = (_search.Text ?? string.Empty).Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _conversationPeople
            : _conversationPeople.Where(x => Contains(x.Name, query) || Contains(x.Subtitle, query)).ToList();
        ReplaceVisibleItems(filtered);
        _empty.Text = filtered.Count == 0 ? "لا توجد محادثات أخرى" : string.Empty;
        _empty.IsVisible = filtered.Count == 0;
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_contactsActive)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            _ = DebouncedContactSearchAsync(e.NewTextValue, _searchCts.Token);
        }
        else ShowConversationItems();
    }

    private async Task DebouncedContactSearchAsync(string? query, CancellationToken token)
    {
        try
        {
            await Task.Delay(380, token);
            await SearchContactsAsync(query, token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task SearchContactsAsync(string? rawQuery, CancellationToken cancellationToken)
    {
        if (!_contactsActive) return;
        var requestVersion = Interlocked.Increment(ref _searchVersion);
        var query = (rawQuery ?? string.Empty).Trim();
        if (query.Length < 3)
        {
            _items = new List<AddMemberPerson>();
            _list.ItemsSource = _items;
            _empty.Text = query.Length == 0 ? "ابحث عن اسم أو رقم أو بريد إلكتروني" : "أدخل 3 أحرف أو أرقام على الأقل";
            _empty.IsVisible = true;
            _busy.IsVisible = _busy.IsRunning = false;
            return;
        }

        _busy.IsVisible = _busy.IsRunning = true;
        _empty.IsVisible = false;
        try
        {
            var results = await _api.SearchGroupMembersAsync(query, cancellationToken);
            if (cancellationToken.IsCancellationRequested || requestVersion != Volatile.Read(ref _searchVersion)) return;
            var currentId = _account.CurrentAccount?.UserId ?? Guid.Empty;
            var people = results
                .Where(x => x.Id != Guid.Empty && x.Id != currentId && !_existingIds.Contains(x.Id))
                .GroupBy(x => x.Id).Select(g => g.First())
                .Select(x => new AddMemberPerson
                {
                    Id = x.Id, Name = x.Name, Subtitle = x.Email,
                    HasPhoto = x.HasProfilePhoto, PhotoVersion = x.ProfilePhotoVersion
                })
                .ToList();
            ReplaceVisibleItems(people);
            _empty.Text = people.Count == 0 ? "لا توجد نتائج" : string.Empty;
            _empty.IsVisible = people.Count == 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (requestVersion == Volatile.Read(ref _searchVersion))
            {
                _empty.Text = ex.Message;
                _empty.IsVisible = true;
            }
        }
        finally
        {
            if (requestVersion == Volatile.Read(ref _searchVersion))
                _busy.IsVisible = _busy.IsRunning = false;
        }
    }

    private void ReplaceVisibleItems(IEnumerable<AddMemberPerson> people)
    {
        var replacement = people.ToList();
        foreach (var item in replacement)
            if (_selected.TryGetValue(item.Id, out var selected)) item.IsSelected = selected.IsSelected;
        _items = replacement;
        _list.ItemsSource = _items;
    }

    private View CreateSelectablePersonRow()
    {
        var row = new Grid
        {
            Padding = new Thickness(16, 9),
            ColumnDefinitions = new ColumnDefinitionCollection { new(52), new(GridLength.Star), new(42) },
            ColumnSpacing = 12,
            MinimumHeightRequest = 72
        };
        var avatar = new Grid { WidthRequest = 48, HeightRequest = 48 };
        var fallback = new Label { FontSize = 17, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        fallback.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        var photo = new Image
        {
            WidthRequest = 48, HeightRequest = 48, Aspect = Aspect.AspectFill, IsVisible = false,
            Clip = new EllipseGeometry { Center = new Point(24, 24), RadiusX = 24, RadiusY = 24 }
        };
        var avatarBorder = new Border { WidthRequest = 48, HeightRequest = 48, Padding = 0, StrokeThickness = 1, StrokeShape = new Ellipse() };
        avatarBorder.SetDynamicResource(Border.BackgroundColorProperty, "HimoSoft");
        avatarBorder.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var avatarContent = new Grid(); avatarContent.Add(fallback); avatarContent.Add(photo); avatarBorder.Content = avatarContent; avatar.Add(avatarBorder);
        row.Add(avatar, 0, 0);
        var labels = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        var name = new Label { FontSize = 14, FontAttributes = FontAttributes.Bold }; name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var subtitle = new Label { FontSize = 11, LineBreakMode = LineBreakMode.TailTruncation }; subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(name); labels.Add(subtitle); row.Add(labels, 1, 0);
        var select = new Button { WidthRequest = 36, HeightRequest = 36, Padding = 0, CornerRadius = 18, FontSize = 17 };
        row.Add(select, 2, 0);

        void Sync(AddMemberPerson person)
        {
            name.Text = person.Name;
            subtitle.Text = person.Subtitle;
            fallback.Text = string.IsNullOrWhiteSpace(person.Name) ? "؟" : person.Name.Trim()[0].ToString();
            photo.IsVisible = false; photo.Source = null; fallback.IsVisible = true;
            if (!string.IsNullOrWhiteSpace(person.PhotoPath) && File.Exists(person.PhotoPath))
            {
                photo.Source = ImageSource.FromFile(person.PhotoPath); photo.IsVisible = true; fallback.IsVisible = false;
            }
            else if (person.HasPhoto) _ = LoadSelectableAvatarAsync(person, photo, fallback, row);
            select.Text = person.IsSelected ? "✓" : "○";
            select.BackgroundColor = person.IsSelected ? Color.FromArgb("#B16CF0") : Colors.Transparent;
            select.TextColor = person.IsSelected ? Colors.White : Color.FromArgb("#B16CF0");
            select.BorderColor = Color.FromArgb("#775C91"); select.BorderWidth = 1;
        }

        row.BindingContextChanged += (_, _) =>
        {
            if (row.BindingContext is AddMemberPerson person) Sync(person);
        };
        void Toggle()
        {
            if (row.BindingContext is not AddMemberPerson person) return;
            person.IsSelected = !person.IsSelected;
            if (person.IsSelected) _selected[person.Id] = person; else _selected.Remove(person.Id);
            Sync(person);
            UpdateSelectionFooter();
        }
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => Toggle(); row.GestureRecognizers.Add(tap);
        select.Clicked += (_, _) => Toggle();
        var divider = new BoxView { HeightRequest = 1, Opacity = 0.12 }; divider.SetDynamicResource(BackgroundColorProperty, "HimoTextSecondary");
        return new VerticalStackLayout { Spacing = 0, Children = { row, divider } };
    }

    private async Task LoadSelectableAvatarAsync(AddMemberPerson person, Image image, Label fallback, View row)
    {
        try
        {
            var path = await ProfilePhotoCache.GetPathAsync(_api, person.Id, person.HasPhoto, person.PhotoVersion);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (row.BindingContext is not AddMemberPerson current || current.Id != person.Id) return;
                image.Source = ImageSource.FromFile(path); image.IsVisible = true; fallback.IsVisible = false;
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Group] Add-member avatar: {ex.Message}"); }
    }

    private void UpdateSelectionFooter()
    {
        var count = _selected.Count;
        _selectedCount.Text = count == 0 ? "لم يتم اختيار أعضاء" : $"تم اختيار {count} أعضاء";
        _addButton.IsEnabled = count > 0 && !_submitting;
        _addButton.Text = count > 0 ? $"إضافة ({count})" : "إضافة";
    }

    private async void AddSelectedClicked(object? sender, EventArgs e)
    {
        if (_submitting || _selected.Count == 0) return;
        _submitting = true;
        _addButton.IsEnabled = false;
        _addButton.Text = "جارٍ الإضافة...";
        try
        {
            var result = await _api.AddGroupMembersAsync(_groupId, _selected.Keys.ToArray());
            _markParentForRefresh();
            await DisplayAlertAsync("أعضاء المجموعة", result.AddedCount > 0 ? $"تمت إضافة {result.AddedCount} أعضاء." : "الأشخاص المحددون أعضاء بالفعل.", "حسنًا");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذرت إضافة الأعضاء", ex.Message, "حسنًا");
        }
        finally
        {
            _submitting = false;
            UpdateSelectionFooter();
        }
    }

    private static bool Contains(string? source, string query) =>
        !string.IsNullOrWhiteSpace(source) && source.Contains(query, StringComparison.OrdinalIgnoreCase);

    protected override void OnDisappearing()
    {
        Interlocked.Increment(ref _searchVersion);
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        base.OnDisappearing();
    }

    private sealed class AddMemberPerson
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public bool HasPhoto { get; init; }
        public long PhotoVersion { get; init; }
        public string? PhotoPath { get; init; }
        public bool IsSelected { get; set; }
    }
}

/// <summary>Half-screen member quick profile sheet.</summary>
public sealed class GroupMemberProfileSheetPage : ContentPage
{
    private readonly Image _avatar = new() { WidthRequest = 96, HeightRequest = 96, Aspect = Aspect.AspectFill, HorizontalOptions = LayoutOptions.Center, IsVisible = false };
    private readonly Label _avatarFallback = new() { FontSize = 34, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };

    public void SetPhoto(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        _avatar.Source = ImageSource.FromFile(path);
        _avatar.IsVisible = true;
        _avatarFallback.IsVisible = false;
    }

    public GroupMemberProfileSheetPage(HimoApiClient.GroupMemberDto member, string? photoPath, Func<Task> message, Func<string, Task> call, Func<Task> info, Func<Task> openPhoto)
    {
        BackgroundColor = Color.FromArgb("#B9000715");
        Shell.SetNavBarIsVisible(this, false);
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(0) };
        var closeArea = new BoxView { BackgroundColor = Colors.Transparent };
        var closeTap = new TapGestureRecognizer(); closeTap.Tapped += async (_, _) => await Navigation.PopModalAsync(); closeArea.GestureRecognizers.Add(closeTap);
        root.Add(closeArea, 0, 0);
        var panel = new VerticalStackLayout { Padding = new Thickness(22, 20, 22, 28), Spacing = 14, HeightRequest = 420 };
        panel.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var drag = new Border { WidthRequest = 42, HeightRequest = 4, StrokeThickness = 0, HorizontalOptions = LayoutOptions.Center };
        drag.SetDynamicResource(BackgroundColorProperty, "HimoBorder"); panel.Add(drag);
        _avatarFallback.Text = string.IsNullOrWhiteSpace(member.Name) ? "؟" : member.Name.Trim()[0].ToString();
        _avatarFallback.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath)) SetPhoto(photoPath);
        _avatar.Clip = new EllipseGeometry { Center = new Point(48, 48), RadiusX = 48, RadiusY = 48 };
        var avatarContent = new Grid();
        avatarContent.Add(_avatarFallback);
        avatarContent.Add(_avatar);
        var avatarBorder = new Border { WidthRequest = 100, HeightRequest = 100, Padding = 2, StrokeThickness = 1, StrokeShape = new Ellipse(), HorizontalOptions = LayoutOptions.Center, Content = avatarContent };
        avatarBorder.SetDynamicResource(Border.StrokeProperty, "HimoAccent"); avatarBorder.SetDynamicResource(Border.BackgroundColorProperty, "HimoCard");
        var avatarTap = new TapGestureRecognizer(); avatarTap.Tapped += async (_, _) => await openPhoto(); avatarBorder.GestureRecognizers.Add(avatarTap);
        panel.Add(avatarBorder);
        var name = new Label { Text = member.Name, FontSize = 21, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); panel.Add(name);
        var role = new Label { Text = member.IsAdmin ? "مشرف المجموعة" : "عضو في المجموعة", FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
        role.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary"); panel.Add(role);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 9 };
        var messageButton = ActionButton("مراسلة", "✉"); messageButton.Clicked += async (_, _) => await message(); actions.Add(messageButton, 0, 0);
        var voiceButton = ActionButton("صوتي", "☎"); voiceButton.Clicked += async (_, _) => await call("audio"); actions.Add(voiceButton, 1, 0);
        var videoButton = ActionButton("فيديو", "▣"); videoButton.Clicked += async (_, _) => await call("video"); actions.Add(videoButton, 2, 0);
        panel.Add(actions);
        var infoButton = ActionButton("معلومات", "ⓘ"); infoButton.Clicked += async (_, _) => await info(); panel.Add(infoButton);
        var spacer = new BoxView { HeightRequest = 1, Opacity = 0.08 }; spacer.SetDynamicResource(BackgroundColorProperty, "HimoTextSecondary"); panel.Add(spacer);
        var close = new Button { Text = "إغلاق", HeightRequest = 44, CornerRadius = 13, BackgroundColor = Colors.Transparent };
        close.SetDynamicResource(Button.TextColorProperty, "HimoTextSecondary"); close.Clicked += async (_, _) => await Navigation.PopModalAsync(); panel.Add(close);
        var panelBorder = new Border { StrokeThickness = 1, Padding = 0, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(25, 25, 0, 0) }, Content = panel };
        panelBorder.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        root.Add(panelBorder, 0, 1);
        Content = root;
    }

    private static Button ActionButton(string text, string glyph)
    {
        var button = new Button { Text = glyph + "\n" + text, HeightRequest = 68, CornerRadius = 14, FontSize = 13, FontAttributes = FontAttributes.Bold };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoSurface");
        button.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder"); button.BorderWidth = 1;
        return button;
    }
}

public sealed class GroupMemberInfoPage : ContentPage
{
    public GroupMemberInfoPage(HimoApiClient.GroupMemberDto member, string? photoPath)
    {
        Title = "معلومات العضو"; Shell.SetNavBarIsVisible(this, false);
        var root = new VerticalStackLayout { Padding = 20, Spacing = 16 };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star) } };
        var back = GroupConversationInfoPage.MakeBack(); back.Clicked += async (_, _) => await Navigation.PopAsync(); header.Add(back, 0, 0);
        var title = new Label { Text = "معلومات العضو", FontSize = 19, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); header.Add(title, 1, 0); root.Add(header);
        var avatar = new Image { WidthRequest = 120, HeightRequest = 120, Aspect = Aspect.AspectFill, HorizontalOptions = LayoutOptions.Center, IsVisible = !string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath) };
        if (avatar.IsVisible) avatar.Source = ImageSource.FromFile(photoPath!);
        avatar.Clip = new EllipseGeometry { Center = new Point(60, 60), RadiusX = 60, RadiusY = 60 };
        var initial = new Label { Text = string.IsNullOrWhiteSpace(member.Name) ? "؟" : member.Name.Trim()[0].ToString(), FontSize = 38, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, IsVisible = !avatar.IsVisible };
        initial.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        var avatarGrid = new Grid { WidthRequest = 120, HeightRequest = 120, HorizontalOptions = LayoutOptions.Center };
        avatarGrid.Add(initial); avatarGrid.Add(avatar);
        var avatarBorder = new Border { WidthRequest = 124, HeightRequest = 124, Padding = 2, StrokeThickness = 1, StrokeShape = new Ellipse(), HorizontalOptions = LayoutOptions.Center, Content = avatarGrid };
        avatarBorder.SetDynamicResource(Border.BackgroundColorProperty, "HimoCard"); avatarBorder.SetDynamicResource(Border.StrokeProperty, "HimoAccent");
        var imageTap = new TapGestureRecognizer(); imageTap.Tapped += async (_, _) => { if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath)) await Navigation.PushAsync(new GroupImagePreviewPage(photoPath, member.Name)); }; avatarBorder.GestureRecognizers.Add(imageTap); root.Add(avatarBorder);
        var name = new Label { Text = member.Name, FontSize = 22, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); root.Add(name);
        root.Add(InfoRow("الصفة", member.IsAdmin ? "مشرف المجموعة" : "عضو"));
        if (!string.IsNullOrWhiteSpace(member.Username))
            root.Add(InfoRow("اسم المستخدم", "@" + member.Username.Trim().TrimStart('@')));
        if (!string.IsNullOrWhiteSpace(member.Status))
            root.Add(InfoRow("الحالة", member.Status));
        root.Add(InfoRow("تاريخ الانضمام", member.JoinedAt.ToLocalTime().ToString("yyyy/MM/dd")));
        Content = new ScrollView { Content = root };
    }

    private static View InfoRow(string title, string value)
    {
        var labels = new VerticalStackLayout { Padding = 14, Spacing = 4 };
        var a = new Label { Text = title, FontSize = 12 }; a.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary"); labels.Add(a);
        var b = new Label { Text = value, FontSize = 15, FontAttributes = FontAttributes.Bold }; b.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); labels.Add(b);
        return ConversationInfoPage.MakeCard(labels);
    }
}

/// <summary>Group-specific notification preferences. Message mute is synced with the server.</summary>
public sealed class GroupNotificationSettingsPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly Conversation _conversation;
    private readonly Guid _groupId;
    private readonly Switch _messageMute = new();
    private readonly Switch _callMute = new();
    private readonly Label _messageTone = new();
    private readonly Label _messageVibration = new();
    private readonly Label _callTone = new();
    private readonly Label _callVibration = new();
    private bool _busy;
    private string Key(string name) => $"himo_group_{_groupId:N}_{name}";

    public GroupNotificationSettingsPage(HimoApiClient api, Conversation conversation)
    {
        _api = api; _conversation = conversation;
        _groupId = Guid.TryParse(conversation.RemoteId, out var id) ? id : Guid.Empty;
        Title = "إعدادات المجموعة"; Shell.SetNavBarIsVisible(this, false);
        BuildPage(); AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) } };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var header = new Grid { Padding = new Thickness(16, 10), ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star) } };
        var back = GroupConversationInfoPage.MakeBack(); back.Clicked += async (_, _) => await Navigation.PopAsync(); header.Add(back, 0, 0);
        var title = new Label { Text = "إعدادات الإشعارات", FontSize = 19, FontAttributes = FontAttributes.Bold }; title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); header.Add(title, 1, 0); root.Add(header, 0, 0);
        var body = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 24), Spacing = 14 };
        body.Add(BuildNotificationSection("الرسائل", _messageMute, (value) => SaveMessageMuteAsync(value),
            BuildChoiceRow("نغمة الإشعار", _messageTone, "message-tone", new[] { "الافتراضية", "هادئة", "نغمة 1", "نغمة 2", "نغمة 3" }),
            BuildChoiceRow("الاهتزاز", _messageVibration, "message-vibration", new[] { "إيقاف", "تلقائي", "قصير", "طويل" })));
        body.Add(BuildNotificationSection("المكالمات", _callMute, value => { Preferences.Default.Set(Key("call-muted"), value); return Task.CompletedTask; },
            BuildChoiceRow("نغمة الرنين", _callTone, "call-tone", new[] { "الافتراضية", "رنين 1", "رنين 2", "رنين 3", "رنين 4" }),
            BuildChoiceRow("الاهتزاز", _callVibration, "call-vibration", new[] { "إيقاف", "تلقائي", "قصير", "طويل" })));
        root.Add(new ScrollView { Content = body }, 0, 1); Content = root;
        _busy = true;
        _messageMute.IsToggled = _conversation.IsMuted;
        _callMute.IsToggled = Preferences.Default.Get(Key("call-muted"), false);
        _messageTone.Text = Preferences.Default.Get(Key("message-tone"), "الافتراضية");
        _messageVibration.Text = Preferences.Default.Get(Key("message-vibration"), "تلقائي");
        _callTone.Text = Preferences.Default.Get(Key("call-tone"), "الافتراضية");
        _callVibration.Text = Preferences.Default.Get(Key("call-vibration"), "تلقائي");
        _busy = false;
    }

    private View BuildNotificationSection(string title, Switch mute, Func<bool, Task> change, View tone, View vibration)
    {
        var stack = new VerticalStackLayout { Spacing = 0 };
        var top = new Grid { Padding = new Thickness(14, 12), ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) } };
        var label = new Label { Text = "كتم " + title, FontSize = 15, FontAttributes = FontAttributes.Bold }; label.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); top.Add(label, 0, 0); top.Add(mute, 1, 0);
        mute.Toggled += async (_, e) =>
        {
            if (_busy) return; _busy = true;
            try { await change(e.Value); }
            catch (Exception ex) { await DisplayAlertAsync("الإشعارات", ex.Message, "حسنًا"); mute.IsToggled = !e.Value; }
            finally { _busy = false; }
        };
        stack.Add(top); stack.Add(new BoxView { HeightRequest = 1, Opacity = 0.12 }); stack.Add(tone); stack.Add(new BoxView { HeightRequest = 1, Opacity = 0.12 }); stack.Add(vibration);
        var card = ConversationInfoPage.MakeCard(stack);
        var container = new VerticalStackLayout { Spacing = 8 };
        var heading = new Label { Text = title, FontSize = 16, FontAttributes = FontAttributes.Bold }; heading.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        container.Add(heading); container.Add(card); return container;
    }

    private View BuildChoiceRow(string title, Label selected, string key, string[] values)
    {
        var grid = new Grid { Padding = new Thickness(14, 12), ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        var labels = new VerticalStackLayout { Spacing = 3 };
        var name = new Label { Text = title, FontSize = 14, FontAttributes = FontAttributes.Bold }; name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); labels.Add(name);
        selected.FontSize = 12; selected.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary"); labels.Add(selected);
        grid.Add(labels, 0, 0);
        var chevron = new Label { Text = "‹", FontSize = 23, VerticalTextAlignment = TextAlignment.Center }; chevron.SetDynamicResource(Label.TextColorProperty, "HimoAccent"); grid.Add(chevron, 1, 0);
        var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) =>
        {
            var choice = await DisplayActionSheetAsync(title, "إلغاء", null, values);
            if (string.IsNullOrWhiteSpace(choice) || choice == "إلغاء") return;
            selected.Text = choice; Preferences.Default.Set(Key(key), choice);
        };
        grid.GestureRecognizers.Add(tap); return grid;
    }

    private async Task SaveMessageMuteAsync(bool muted)
    {
        await _api.SetConversationActionAsync(_groupId, muted ? "mute" : "unmute");
        Preferences.Default.Set(Key("message-muted"), muted);
        _conversation.IsMuted = muted;
    }
}

public sealed class GroupInviteQrPage : ContentPage
{
    public GroupInviteQrPage(string link, string groupName)
    {
        Title = "رابط المجموعة"; Shell.SetNavBarIsVisible(this, false);
        var root = new VerticalStackLayout { Padding = 20, Spacing = 16, VerticalOptions = LayoutOptions.Center };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star) } };
        var back = GroupConversationInfoPage.MakeBack(); back.Clicked += async (_, _) => await Navigation.PopAsync(); header.Add(back, 0, 0);
        var title = new Label { Text = "رمز دعوة المجموعة", FontSize = 19, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); header.Add(title, 1, 0); root.Add(header);
        var group = new Label { Text = groupName, FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; group.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); root.Add(group);
        var qr = new ZXing.Net.Maui.Controls.BarcodeGeneratorView { Format = ZXing.Net.Maui.BarcodeFormat.QrCode, Value = link, WidthRequest = 250, HeightRequest = 250, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, BackgroundColor = Colors.White, ForegroundColor = Color.FromArgb("#160A2C") };
        var frame = new Border { WidthRequest = 270, HeightRequest = 270, Padding = 10, BackgroundColor = Colors.White, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 22 }, HorizontalOptions = LayoutOptions.Center, Content = qr };
        root.Add(frame);
        Content = root;
    }
}

/// <summary>Share hub for the group's invite QR and copyable invite link.</summary>
public sealed class GroupSharePage : ContentPage
{
    public GroupSharePage(string link, string groupName)
    {
        Title = "مشاركة";
        Shell.SetNavBarIsVisible(this, false);
        FlowDirection = FlowDirection.RightToLeft;
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) } };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var header = new Grid { Padding = new Thickness(16, 10), ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star), new(44) } };
        var back = GroupConversationInfoPage.MakeBack();
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        var title = new Label { Text = "مشاركة", FontSize = 19, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        header.Add(title, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0);
        root.Add(header, 0, 0);

        var body = new VerticalStackLayout { Padding = new Thickness(18, 18, 18, 24), Spacing = 14, VerticalOptions = LayoutOptions.Start };
        var group = new Label { Text = groupName, FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        group.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        body.Add(group);
        body.Add(BuildShareOption("▦", "رمز QR", "عرض الرمز للمسح", async () =>
            await Navigation.PushAsync(new GroupInviteQrPage(link, groupName))));
        body.Add(BuildShareOption("↗", "رابط المجموعة", "عرض الرابط ونسخه", async () =>
            await Navigation.PushAsync(new GroupInviteLinkPage(link, groupName))));
        root.Add(new ScrollView { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 0, 1);
        Content = root;
        AppLocalization.AttachToPage(this);
    }

    private static View BuildShareOption(string glyph, string title, string subtitle, Func<Task> action)
    {
        var grid = new Grid
        {
            Padding = new Thickness(15, 14),
            ColumnDefinitions = new ColumnDefinitionCollection { new(50), new(GridLength.Star), new(22) },
            ColumnSpacing = 12
        };
        var icon = new Border { WidthRequest = 46, HeightRequest = 46, StrokeThickness = 1, Padding = 0, StrokeShape = new RoundRectangle { CornerRadius = 14 } };
        icon.SetDynamicResource(Border.BackgroundColorProperty, "HimoSoft");
        icon.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var iconLabel = new Label { Text = glyph, FontSize = 23, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        iconLabel.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        icon.Content = iconLabel;
        grid.Add(icon, 0, 0);
        var labels = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        var name = new Label { Text = title, FontSize = 15, FontAttributes = FontAttributes.Bold };
        name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var hint = new Label { Text = subtitle, FontSize = 11 };
        hint.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(name); labels.Add(hint); grid.Add(labels, 1, 0);
        var arrow = new Label { Text = "‹", FontSize = 25, VerticalTextAlignment = TextAlignment.Center };
        arrow.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        grid.Add(arrow, 2, 0);
        var card = ConversationInfoPage.MakeCard(grid);
        var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await action(); card.GestureRecognizers.Add(tap);
        return card;
    }
}

/// <summary>Invite-link details with a dedicated copy control.</summary>
public sealed class GroupInviteLinkPage : ContentPage
{
    public GroupInviteLinkPage(string link, string groupName)
    {
        Title = "رابط المجموعة";
        Shell.SetNavBarIsVisible(this, false);
        FlowDirection = FlowDirection.RightToLeft;
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) } };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        var header = new Grid { Padding = new Thickness(16, 10), ColumnDefinitions = new ColumnDefinitionCollection { new(44), new(GridLength.Star), new(44) } };
        var back = GroupConversationInfoPage.MakeBack(); back.Clicked += async (_, _) => await Navigation.PopAsync(); header.Add(back, 0, 0);
        var title = new Label { Text = "رابط المجموعة", FontSize = 19, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); header.Add(title, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0); root.Add(header, 0, 0);

        var body = new VerticalStackLayout { Padding = new Thickness(20, 28), Spacing = 16, VerticalOptions = LayoutOptions.Start };
        var group = new Label { Text = groupName, FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        group.SetDynamicResource(Label.TextColorProperty, "HimoPrimary"); body.Add(group);
        var linkLabel = new Label { Text = link, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap, FlowDirection = FlowDirection.LeftToRight, HorizontalTextAlignment = TextAlignment.Start };
        linkLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var linkCard = ConversationInfoPage.MakeCard(new VerticalStackLayout { Padding = 15, Spacing = 10, Children = { linkLabel } });
        body.Add(linkCard);
        var copyButton = new Button { Text = "⧉  نسخ الرابط", HeightRequest = 50, CornerRadius = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        copyButton.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        copyButton.Clicked += async (_, _) =>
        {
            try
            {
                await Clipboard.Default.SetTextAsync(link);
                await DisplayAlertAsync("رابط المجموعة", "تم نسخ الرابط.", "حسنًا");
            }
            catch (Exception ex) { await DisplayAlertAsync("تعذر النسخ", ex.Message, "حسنًا"); }
        };
        body.Add(copyButton);
        root.Add(new ScrollView { Content = body }, 0, 1);
        Content = root;
        AppLocalization.AttachToPage(this);
    }
}

public sealed class GroupImagePreviewPage : ContentPage
{
    public GroupImagePreviewPage(string path, string title)
    {
        Title = title; BackgroundColor = Colors.Black; Shell.SetNavBarIsVisible(this, false);
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) }, BackgroundColor = Colors.Black };
        var back = new Button { Text = "‹", WidthRequest = 44, HeightRequest = 44, CornerRadius = 14, BackgroundColor = Color.FromArgb("#44202020"), TextColor = Colors.White, Margin = 12 };
        back.Clicked += async (_, _) =>
        {
            if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
            else await Navigation.PopAsync();
        };
        root.Add(back, 0, 0);
        root.Add(new Image { Source = ImageSource.FromFile(path), Aspect = Aspect.AspectFit }, 0, 1); Content = root;
    }
}
