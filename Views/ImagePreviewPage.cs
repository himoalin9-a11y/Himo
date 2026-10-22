using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Himo.Views;

/// <summary>
/// Full-screen image viewer with pinch-to-zoom and pan gestures.
/// Uses the shared white close icon so it remains legible over the dark viewer.
/// </summary>
public sealed class ImagePreviewPage : Himo.Services.LocalizedContentPage
{
    private readonly Image _image;
    private double _startScale = 1;
    private double _startX;
    private double _startY;

    public ImagePreviewPage(string filePath, string title)
    {
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Colors.Black;
        NavigationPage.SetHasNavigationBar(this, false);
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        _image = new Image
        {
            Source = ImageSource.FromFile(filePath),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Scale = 1,
            AutomationId = "PreviewImage",
        };
        SemanticProperties.SetDescription(_image, string.IsNullOrWhiteSpace(title) ? "معاينة الصورة" : title);

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        _image.GestureRecognizers.Add(pinch);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        _image.GestureRecognizers.Add(pan);

        var closeButton = new ImageButton
        {
            Source = ImageSource.FromFile("himo_ui_close_white.png"),
            WidthRequest = 42,
            HeightRequest = 42,
            Padding = 10,
            BackgroundColor = Colors.Transparent,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            AutomationId = "CloseImagePreviewButton",
        };
        SemanticProperties.SetDescription(closeButton, "إغلاق معاينة الصورة");
        closeButton.Clicked += async (_, _) => await CloseAsync();

        var titleLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(title) ? "معاينة الصورة" : title,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalOptions = LayoutOptions.Center,
        };

        var header = new Grid
        {
            Padding = new Thickness(14, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb("#D91A1A1A"), 0f),
                    new GradientStop(Color.FromArgb("#661A1A1A"), 1f),
                }
            },
        };
        header.Add(closeButton, 0, 0);
        header.Add(titleLabel, 1, 0);

        var hint = new Label
        {
            Text = "استخدم إصبعين للتقريب، واسحب الصورة بعد تكبيرها",
            FontSize = 12,
            TextColor = Color.FromArgb("#E6FFFFFF"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        var hintContainer = new Border
        {
            Margin = new Thickness(18, 0, 18, 18),
            Padding = new Thickness(16, 10),
            BackgroundColor = Color.FromArgb("#99161616"),
            Stroke = Color.FromArgb("#33FFFFFF"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Content = hint,
            HorizontalOptions = LayoutOptions.Center,
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            BackgroundColor = Colors.Black,
        };
        root.Add(header, 0, 0);
        root.Add(_image, 0, 1);
        root.Add(hintContainer, 0, 2);
        Content = root;
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
                _image.Scale = Math.Clamp(_startScale * e.Scale, 1, 4);
                break;
            case GestureStatus.Completed:
                if (_image.Scale <= 1.01)
                    ResetImageTransform();
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

    private void ResetImageTransform()
    {
        _image.Scale = 1;
        _image.TranslationX = 0;
        _image.TranslationY = 0;
    }

    private async Task CloseAsync()
    {
        try
        {
            await Navigation.PopModalAsync();
        }
        catch (InvalidOperationException)
        {
            // The modal may already have been dismissed by the system back gesture.
        }
        catch (ArgumentException)
        {
            // Some navigation containers report an invalid target after dismissal.
        }
    }
}
