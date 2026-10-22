using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>Contains account-wide session and deletion actions, separate from general settings.</summary>
public sealed class AccountActionsPage : Himo.Services.LocalizedContentPage
{
    private const string PushTokenKey = "himo_registered_fcm_token";
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ChatService _chat;
    private readonly INotificationService _notifications;
    private int _actionInProgress;

    public AccountActionsPage(HimoApiClient api, AccountService account, ChatService chat, INotificationService notifications)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        Title = "الحساب";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        Content = BuildPage();
    }

    private View BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            RowSpacing = 0
        };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            Padding = new Thickness(18, 14, 18, 12),
            ColumnDefinitions = { new ColumnDefinition(new GridLength(48)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(48)) },
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.RightToLeft
        };
        var back = new Button
        {
            Text = "‹", FontSize = 34, FontAttributes = FontAttributes.Bold,
            WidthRequest = 42, HeightRequest = 42, Padding = 0,
            CornerRadius = 15, BorderWidth = 1,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center
        };
        back.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        back.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        back.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        back.Clicked += BackClicked;
        header.Add(back, 0, 0);

        var titleStack = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        var title = new Label { Text = "الحساب", FontSize = 24, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Start };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var subtitle = new Label { Text = "إدارة جلساتك وإجراءات حسابك", FontSize = 12, HorizontalTextAlignment = TextAlignment.Start };
        subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        titleStack.Add(title);
        titleStack.Add(subtitle);
        header.Add(titleStack, 1, 0);

        var content = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 24), Spacing = 12 };
        var intro = new Label
        {
            Text = "اختر الإجراء الذي تريد تنفيذه. حذف الحساب نهائي ولا يمكن التراجع عنه.",
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(2, 0, 2, 4)
        };
        intro.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        content.Add(intro);

        content.Add(BuildActionCard(
            "الخروج من جميع الأجهزة",
            "إنهاء جلسات Himo المفتوحة على أجهزتك الأخرى أيضًا.",
            "تنفيذ الخروج من جميع الأجهزة",
            "himo_pro_ui_settings_icon_lock.png",
            "HimoAccent",
            LogoutAllClicked));

        content.Add(BuildActionCard(
            "تسجيل الخروج",
            "إنهاء الجلسة على هذا الجهاز فقط.",
            "تسجيل الخروج من هذا الجهاز",
            "himo_pro_ui_settings_icon_user.png",
            "HimoAccent",
            LogoutClicked));

        content.Add(BuildActionCard(
            "حذف الحساب نهائيًا",
            "حذف الحساب وبياناته ومحادثاته من الخادم. لا يمكن التراجع عن الحذف.",
            "حذف الحساب نهائيًا",
            "himo_pro_ui_settings_icon_trash.png",
            "HimoDanger",
            DeleteAccountClicked,
            destructive: true));

        root.Add(header, 0, 0);
        root.Add(new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 0, 1);
        return root;
    }

    private Border BuildActionCard(string title, string description, string buttonText, string iconSource,
        string accentResource, EventHandler clicked, bool destructive = false)
    {
        var card = new Border { Padding = new Thickness(14), StrokeThickness = 1.2 };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, destructive ? "HimoDanger" : "HimoBorder");
        card.StrokeShape = new RoundRectangle { CornerRadius = 19 };

        var layout = new VerticalStackLayout { Spacing = 10 };
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(44) },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.RightToLeft
        };
        var texts = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        var titleLabel = new Label { Text = title, FontSize = 15, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Start };
        titleLabel.SetDynamicResource(Label.TextColorProperty, destructive ? "HimoDanger" : "HimoPrimary");
        var descriptionLabel = new Label { Text = description, FontSize = 11, LineBreakMode = LineBreakMode.WordWrap, HorizontalTextAlignment = TextAlignment.Start };
        descriptionLabel.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        texts.Add(titleLabel);
        texts.Add(descriptionLabel);
        row.Add(texts, 0, 0);

        var iconBox = new Border { WidthRequest = 42, HeightRequest = 42, StrokeThickness = 0, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        iconBox.SetDynamicResource(VisualElement.BackgroundColorProperty, destructive ? "HimoDangerSoft" : "HimoSoft");
        iconBox.StrokeShape = new RoundRectangle { CornerRadius = 21 };
        iconBox.Content = new Image { Source = iconSource, WidthRequest = 22, HeightRequest = 22, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        row.Add(iconBox, 1, 0);
        layout.Add(row);

        var actionButton = new Button { Text = buttonText, HeightRequest = 46, CornerRadius = 14, FontSize = 13, FontAttributes = FontAttributes.Bold };
        actionButton.SetDynamicResource(VisualElement.BackgroundColorProperty, destructive ? "HimoDangerSoft" : "HimoSoft");
        actionButton.SetDynamicResource(Button.TextColorProperty, destructive ? "HimoDanger" : "HimoPrimary");
        if (destructive)
            actionButton.SetDynamicResource(Button.BorderColorProperty, "HimoDanger");
        actionButton.BorderWidth = destructive ? 1 : 0;
        actionButton.Clicked += clicked;
        layout.Add(actionButton);

        card.Content = layout;
        return card;
    }

    private async void LogoutAllClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _actionInProgress, 1) != 0) return;
        try
        {
            var confirm = await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("الخروج من جميع الأجهزة"),
                Himo.Services.AppLocalization.TranslateSourceText("سيتم إنهاء جميع جلسات Himo وإيقاف إشعارات الأجهزة المسجلة. هل تريد المتابعة؟"),
                Himo.Services.AppLocalization.TranslateSourceText("متابعة"), Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
            if (!confirm) return;

            try
            {
                await _api.LogoutAllAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تسجيل الخروج"), $"تعذر تنفيذ العملية: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            await CompleteLocalSignOutAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _actionInProgress, 0);
        }
    }

    private async void LogoutClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _actionInProgress, 1) != 0) return;
        try
        {
            var confirm = await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تسجيل الخروج"), "هل تريد تسجيل الخروج من هذا الجهاز فقط؟", Himo.Services.AppLocalization.TranslateSourceText("خروج"), Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
            if (!confirm) return;

            string? logoutError = null;
            try
            {
#if ANDROID
                if (Application.Current is App app)
                    await app.UnregisterCurrentPushTokenAsync();
#endif
                await _api.LogoutAsync();
            }
            catch (Exception ex)
            {
                logoutError = ex.Message;
            }

            await CompleteLocalSignOutAsync();
            if (!string.IsNullOrWhiteSpace(logoutError))
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تسجيل الخروج"), "تم تسجيل الخروج من هذا الجهاز، لكن تعذر إكمال العملية على الخادم. يمكنك تسجيل الدخول لاحقًا.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        finally
        {
            Interlocked.Exchange(ref _actionInProgress, 0);
        }
    }

    private async void DeleteAccountClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _actionInProgress, 1) != 0) return;
        try
        {
            var confirm = await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("حذف الحساب نهائيًا"),
                "سيتم حذف حسابك وبياناته ومحادثاتك من الخادم نهائيًا. لا يمكن التراجع عن هذه العملية.",
                Himo.Services.AppLocalization.TranslateSourceText("حذف نهائي"), Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
            if (!confirm) return;

            try
            {
                await _api.DeleteAccountAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("حذف الحساب"), $"تعذر حذف الحساب: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            await CompleteLocalSignOutAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _actionInProgress, 0);
        }
    }

    private async Task CompleteLocalSignOutAsync()
    {
        try { await _notifications.ClearAllAsync(); } catch { }
        _account.SignOut();
        _chat.ClearAll();
        _api.ClearToken();
        Preferences.Default.Remove(PushTokenKey);

        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is not null)
            window.Page = new LoginPage(_account, _api);
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..");
    }
}
