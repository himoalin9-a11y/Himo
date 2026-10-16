using Microsoft.Maui.Controls;

namespace Himo.Views;

/// <summary>
/// Full-screen in-app image viewer. Supports pinch zoom and pan without leaving Himo.
/// </summary>
public sealed class ImagePreviewPage : ContentPage
{
    private readonly Image _image;
    private readonly Grid _root;
    private readonly Label _title;
    private double _startScale = 1;
    private double _startX;
    private double _startY;

    public ImagePreviewPage(string filePath, string title)
    {
        BackgroundColor = Colors.Black;
        NavigationPage.SetHasNavigationBar(this, false);

        _image = new Image
        {
            Source = ImageSource.FromFile(filePath),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Scale = 1,
        };

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        _image.GestureRecognizers.Add(pinch);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        _image.GestureRecognizers.Add(pan);

        var closeButton = new Button
        {
            Text = "×",
            FontSize = 30,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("66000000"),
            WidthRequest = 48,
            HeightRequest = 48,
            CornerRadius = 24,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 14, 14, 0),
        };
        closeButton.Clicked += async (_, _) => await CloseAsync();

        _title = new Label
        {
            Text = title,
            FontSize = 13,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            BackgroundColor = Color.FromArgb("66000000"),
            Padding = new Thickness(14, 7),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(20, 0, 20, 18),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };

        _root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Star)
            }
        };
        _root.Children.Add(_image);
        _root.Children.Add(closeButton);
        _root.Children.Add(_title);

        Content = _root;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _image.Scale = 1;
        _image.TranslationX = 0;
        _image.TranslationY = 0;
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _startScale = _image.Scale;
                break;
            case GestureStatus.Running:
                var next = _startScale * e.Scale;
                _image.Scale = Math.Clamp(next, 1, 4);
                break;
            case GestureStatus.Completed:
                if (_image.Scale <= 1.01)
                {
                    _image.Scale = 1;
                    _image.TranslationX = 0;
                    _image.TranslationY = 0;
                }
                break;
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_image.Scale <= 1.01)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _startX = _image.TranslationX;
                _startY = _image.TranslationY;
                break;
            case GestureStatus.Running:
                _image.TranslationX = _startX + e.TotalX;
                _image.TranslationY = _startY + e.TotalY;
                break;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        MainThread.BeginInvokeOnMainThread(async () => await CloseAsync());
        return true;
    }

    private async Task CloseAsync()
    {
        try
        {
            await Navigation.PopModalAsync();
        }
        catch
        {
            // Modal may already have been closed by the system back gesture.
        }
    }
}
