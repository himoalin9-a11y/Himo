namespace Himo.Views;

public partial class MediaCapturePreviewPage : ContentPage
{
    private readonly TaskCompletionSource<CameraCaptureResult?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CameraCaptureResult _capture;
    private bool _closing;

    private MediaCapturePreviewPage(CameraCaptureResult capture)
    {
        InitializeComponent();
        _capture = capture;
        PreviewImage.Source = ImageSource.FromFile(capture.FilePath);
        PreviewImage.Aspect = Aspect.AspectFit;
    }

    public static async Task<CameraCaptureResult?> PreviewAsync(CameraCaptureResult capture)
    {
        if (capture is null || string.IsNullOrWhiteSpace(capture.FilePath) || !File.Exists(capture.FilePath))
            return null;

        var page = new MediaCapturePreviewPage(capture);
        var navigation = Shell.Current?.Navigation
            ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (navigation is null)
            return null;

        await navigation.PushModalAsync(page, animated: true);
        return await page._completion.Task;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // The preview must remain alive while the editor is presented on top of it.
        PreviewImage.Aspect = Aspect.AspectFit;
    }

    protected override void OnDisappearing()
    {
        // Do not complete the task merely because MediaEditorPage is presented over us.
        // The task is completed only by Send, Cancel, or the back button.
        base.OnDisappearing();
    }

    private async void EditClicked(object? sender, EventArgs e)
    {
        try
        {
            var edited = await MediaEditorPage.EditAsync(_capture);
            if (edited is null)
                return;

            _capture = edited;
            PreviewImage.Source = null;
            PreviewImage.Source = ImageSource.FromFile(_capture.FilePath);
            PreviewImage.Aspect = Aspect.AspectFit;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("التعديل", $"تعذر فتح محرر الصورة: {ex.Message}", "حسنًا");
        }
    }

    private async void SendClicked(object? sender, EventArgs e) => await CloseAsync(_capture);

    private async void CancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    protected override bool OnBackButtonPressed()
    {
        MainThread.BeginInvokeOnMainThread(async () => await CloseAsync(null));
        return true;
    }

    private async Task CloseAsync(CameraCaptureResult? result)
    {
        if (_closing)
            return;

        _closing = true;
        _completion.TrySetResult(result);
        try
        {
            await Navigation.PopModalAsync(animated: true);
        }
        catch
        {
        }
    }
}
