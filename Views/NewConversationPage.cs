using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>
/// Dedicated entry page opened from the floating plus button.
/// It replaces the platform ActionSheet with two clear, touch-friendly actions.
/// </summary>
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
            Padding = new Thickness(18, 12, 18, 20),
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Star),
                new(GridLength.Auto)
            },
            RowSpacing = 18
        };
        root.SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            HeightRequest = 54,
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(48), new(GridLength.Star), new(48)
            },
            ColumnSpacing = 8
        };
        var back = new Button
        {
            Text = "‹",
            FontSize = 31,
            Padding = 0,
            WidthRequest = 44,
            HeightRequest = 44,
            CornerRadius = 15,
            BackgroundColor = Colors.Transparent
        };
        back.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        back.Clicked += BackClicked;
        header.Add(back, 0, 0);

        var heading = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        var title = new Label
        {
            Text = "إنشاء جديد",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var subtitle = new Label
        {
            Text = "اختر ما تريد البدء به",
            FontSize = 11,
            HorizontalTextAlignment = TextAlignment.Center
        };
        subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        heading.Add(title);
        heading.Add(subtitle);
        header.Add(heading, 1, 0);
        header.Add(new BoxView { Opacity = 0 }, 2, 0);
        root.Add(header, 0, 0);

        var introContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(48), new(GridLength.Star) },
            ColumnSpacing = 12
        };
        introContent.Add(CreateIntroIcon(), 0, 0);
        introContent.Add(CreateIntroText(), 1, 0);
        var intro = new Border
        {
            Padding = new Thickness(16, 15),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            Content = introContent
        };
        intro.SetDynamicResource(BackgroundColorProperty, "HimoSurfaceElevated");
        intro.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        root.Add(intro, 0, 1);

        var options = new VerticalStackLayout { Spacing = 12 };
        options.Add(CreateOptionCard(
            "👥", "إضافة مجموعة", "اجمع الأشخاص في مساحة واحدة للمحادثة والتواصل.", true,
            async () => await OpenGroupCreationAsync()));
        options.Add(CreateOptionCard(
            "✉", "محادثة جديدة", "ابحث عن شخص وابدأ محادثة خاصة معه.", false,
            async () => await OpenNewChatAsync()));
        root.Add(options, 0, 2);

        var hint = new Label
        {
            Text = "يمكنك العودة إلى المحادثات في أي وقت من زر الرجوع.",
            FontSize = 11,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        hint.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        root.Add(hint, 0, 4);

        Content = root;
    }

    private View CreateIntroIcon()
    {
        var icon = new Label
        {
            Text = "＋",
            FontSize = 29,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        icon.SetDynamicResource(Label.TextColorProperty, "HimoOnAccent");
        var circle = new Border
        {
            WidthRequest = 46,
            HeightRequest = 46,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = icon,
            VerticalOptions = LayoutOptions.Center
        };
        circle.SetDynamicResource(BackgroundColorProperty, "HimoAccent");
        return circle;
    }

    private View CreateIntroText()
    {
        var stack = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var title = new Label { Text = "ابدأ تواصلك بطريقتك", FontSize = 14, FontAttributes = FontAttributes.Bold };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var body = new Label { Text = "أنشئ مجموعة للأصدقاء أو افتح محادثة مباشرة.", FontSize = 11, LineBreakMode = LineBreakMode.WordWrap };
        body.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        stack.Add(title);
        stack.Add(body);
        return stack;
    }

    private Border CreateOptionCard(string symbol, string titleText, string description, bool highlighted, Func<Task> action)
    {
        var iconLabel = new Label
        {
            Text = symbol,
            FontSize = 25,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        iconLabel.SetDynamicResource(Label.TextColorProperty, highlighted ? "HimoOnAccent" : "HimoAccent");
        var iconBorder = new Border
        {
            WidthRequest = 54,
            HeightRequest = 54,
            StrokeThickness = highlighted ? 0 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(17) },
            Content = iconLabel,
            VerticalOptions = LayoutOptions.Center
        };
        if (highlighted)
            iconBorder.SetDynamicResource(BackgroundColorProperty, "HimoAccent");
        else
        {
            iconBorder.SetDynamicResource(BackgroundColorProperty, "HimoSurfaceElevated");
            iconBorder.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        }

        var labels = new VerticalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
        var title = new Label { Text = titleText, FontSize = 15, FontAttributes = FontAttributes.Bold };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var detail = new Label { Text = description, FontSize = 11, LineBreakMode = LineBreakMode.WordWrap };
        detail.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(title);
        labels.Add(detail);

        var arrow = new Label
        {
            Text = "‹",
            FontSize = 28,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        };
        arrow.SetDynamicResource(Label.TextColorProperty, "HimoAccent");

        var content = new Grid
        {
            Padding = new Thickness(14, 14),
            ColumnDefinitions = new ColumnDefinitionCollection { new(58), new(GridLength.Star), new(24) },
            ColumnSpacing = 12
        };
        content.Add(iconBorder, 0, 0);
        content.Add(labels, 1, 0);
        content.Add(arrow, 2, 0);

        var card = new Border
        {
            Content = content,
            StrokeThickness = highlighted ? 1.4 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(21) }
        };
        card.SetDynamicResource(BackgroundColorProperty, highlighted ? "HimoSurfaceElevated" : "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, highlighted ? "HimoAccent" : "HimoBorder");
        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await action())
        });
        SemanticProperties.SetDescription(card, $"{titleText}. {description}");
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
