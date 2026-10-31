using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>Dedicated entry screen for starting a group or private conversation.</summary>
public sealed class NewConversationPage : ContentPage
{
    private bool _navigating;

    public NewConversationPage()
    {
        Title = "إنشاء جديد";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid
        {
            Padding = new Thickness(18, 12, 18, 22),
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star)
            },
            RowSpacing = 18
        };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            HeightRequest = 54,
            ColumnDefinitions = new ColumnDefinitionCollection { new(48), new(GridLength.Star), new(48) },
            ColumnSpacing = 8
        };

        var back = new ImageButton
        {
            Source = "himo_theme_p_neon_violet_himo_icon_arrow_back_purple.png",
            WidthRequest = 42,
            HeightRequest = 42,
            Padding = 9,
            BackgroundColor = Colors.Transparent,
            Aspect = Aspect.AspectFit
        };
        back.Clicked += BackClicked;
        header.Add(back, 0, 0);

        var title = new Label
        {
            Text = "إنشاء جديد",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        header.Add(title, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0);
        root.Add(header, 0, 0);

        var content = new VerticalStackLayout
        {
            Spacing = 14,
            VerticalOptions = LayoutOptions.Start,
            Padding = new Thickness(0, 14, 0, 12)
        };

        content.Add(CreateOptionCard(
            "himo_theme_p_neon_violet_himo_s7_notif_group.png",
            "إضافة مجموعة",
            "اجمع أصدقاءك في محادثة واحدة",
            true,
            OpenGroupCreationAsync));

        content.Add(CreateOptionCard(
            "himo_theme_p_neon_violet_himo_s7_notif_message.png",
            "محادثة جديدة",
            "ابدأ محادثة خاصة",
            false,
            OpenNewChatAsync));

        var scroll = new ScrollView
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };
        root.Add(scroll, 0, 1);
        Content = root;
    }

    private Border CreateOptionCard(string image, string titleText, string description, bool highlighted, Func<Task> action)
    {
        var iconSurface = new Border
        {
            WidthRequest = 62,
            HeightRequest = 62,
            Padding = 13,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) },
            Content = new Image { Source = image, Aspect = Aspect.AspectFit }
        };
        iconSurface.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
        iconSurface.SetDynamicResource(Border.StrokeProperty, highlighted ? "HimoAccent" : "HimoBorder");

        var labels = new VerticalStackLayout
        {
            Spacing = 5,
            VerticalOptions = LayoutOptions.Center
        };
        var title = new Label
        {
            Text = titleText,
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var detail = new Label
        {
            Text = description,
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap
        };
        detail.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(title);
        labels.Add(detail);

        var arrow = new Label
        {
            Text = "‹",
            FontSize = 29,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };
        arrow.SetDynamicResource(Label.TextColorProperty, "HimoAccent");

        var row = new Grid
        {
            Padding = new Thickness(16, 18),
            HeightRequest = 112,
            ColumnDefinitions = new ColumnDefinitionCollection { new(66), new(GridLength.Star), new(24) },
            ColumnSpacing = 13
        };
        row.Add(iconSurface, 0, 0);
        row.Add(labels, 1, 0);
        row.Add(arrow, 2, 0);

        var card = new Border
        {
            Content = row,
            StrokeThickness = highlighted ? 1.5 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(23) }
        };
        card.SetDynamicResource(BackgroundColorProperty, "HimoSurfaceElevated");
        card.SetDynamicResource(Border.StrokeProperty, highlighted ? "HimoAccent" : "HimoBorder");
        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await action())
        });
        SemanticProperties.SetDescription(card, titleText);
        return card;
    }

    private async Task OpenGroupCreationAsync()
    {
        if (_navigating || Shell.Current is null) return;
        _navigating = true;
        try { await Shell.Current.GoToAsync("CreateGroupPage"); }
        finally { _navigating = false; }
    }

    private async Task OpenNewChatAsync()
    {
        if (_navigating || Shell.Current is null) return;
        _navigating = true;
        try { await SwipePageTransition.NavigateAsync(this, "search", SwipeDirection.Left); }
        finally { _navigating = false; }
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (_navigating || Shell.Current is null) return;
        _navigating = true;
        try { await Shell.Current.GoToAsync(".."); }
        finally { _navigating = false; }
    }
}
