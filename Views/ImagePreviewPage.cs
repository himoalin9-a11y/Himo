using Microsoft.Maui.Controls;

namespace Himo.Views;

/// <summary>
/// In-app image viewer used by ChatPage. Keeps the user inside Himo instead of
/// handing the attachment to an external gallery/file application.
/// </summary>
public sealed class ImagePreviewPage : ContentPage
{
    private readonly Image _image;
    private double _currentScale = 1d;
    private double _startScale = 1d;

    public ImagePreviewPage(string imagePath, string title)
    {
        BackgroundColor = Colors.Black;
        NavigationPage.SetHasNavigationBar(this, false);
        Shell.SetNavBarIsVisible(this, false);

        _image = new Image
        {
            Source = ImageSource.FromFile(imagePath),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(8, 60, 8, 42)
        };

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        _image.GestureRecognizers.Add(pinch);

        var closeButton = new Button
        {
            Text = "✕",
            FontSize = 24,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("66000000"),
            WidthRequest = 48,
            HeightRequest = 48,
            CornerRadius = 24,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 18, 14, 0),
            ZIndex = 10
        };
        closeButton.Clicked += async (_, _) => await CloseAsync();

        var titleLabel = new Label
        {
            Text = title,
            TextColor = Colors.White,
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            Margin = new Thickness(20, 0, 20, 14),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            ZIndex = 10
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Star)
            }
        };
        layout.Children.Add(_image);
        layout.Children.Add(closeButton);
        layout.Children.Add(titleLabel);

        Content = layout;
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _startScale = _currentScale;
                break;

            case GestureStatus.Running:
                _currentScale = Math.Clamp(_startScale * e.Scale, 1d, 4d);
                _image.Scale = _currentScale;
                break;

            case GestureStatus.Completed:
                _currentScale = Math.Clamp(_currentScale, 1d, 4d);
                break;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync();
        return true;
    }

    private async Task CloseAsync()
    {
        if (Navigation.ModalStack.Count > 0)
            await Navigation.PopModalAsync(true);
    }
}
