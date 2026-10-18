namespace Himo.Views
{
    internal static class CameraPageHelpers
    {

        public static async Task<MediaEditorCaptureResult?> OpenAsync(
            CameraCaptureMode initialMode = CameraCaptureMode.Photo)
        {
            var page = new CameraPage(initialMode);
            var navigation = Shell.Current?.Navigation
                ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;

            if (navigation is null)
                return null;

            await navigation.PushModalAsync(page, animated: true);
            return await page._completion.Task;
        }
    }
}