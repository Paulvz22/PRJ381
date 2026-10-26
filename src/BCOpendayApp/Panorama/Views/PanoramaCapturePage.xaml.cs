using BCOpendayApp.AR.Sensors;
using BCOpendayApp.Panorama.ViewModels;
using BCOpendayApp.Views;
using CommunityToolkit.Maui.Views;

namespace BCOpendayApp.Panorama.Views;

/// <summary>
/// Camera preview for <see cref="PanoramaCaptureViewModel"/>. Same pattern as the AR page: permissions are
/// asked one at a time first, and only then is the CameraView created (CommunityToolkit.Maui.Camera 4.0.0 asks
/// for camera + microphone itself as soon as the view exists, which crashed the app when it raced the
/// location prompt). The view model takes photos through <see cref="PanoramaCaptureViewModel.CapturePhoto"/>.
/// </summary>
public partial class PanoramaCapturePage : BasePage
{
    private readonly PanoramaCaptureViewModel viewModel;
    private CameraView? camera;
    private bool isOnScreen;
    private CancellationTokenSource? cameraStart;

    public PanoramaCapturePage(PanoramaCaptureViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        viewModel.CapturePhoto = CapturePhotoAsync;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isOnScreen = true;
        ArPlatform.EnterArMode(); // portrait (the stitcher expects portrait photos) and screen kept on

        try
        {
            var (cameraAllowed, locationAllowed) = await ArPlatform.RequestPermissionsAsync(needCamera: true);
            if (!isOnScreen)
            {
                return;
            }

            if (!await viewModel.PrepareAsync(locationAllowed) || !isOnScreen)
            {
                return;
            }

            if (!cameraAllowed)
            {
                await DisplayAlert("Camera permission is off", "Allow the camera in the phone's Settings to capture 360° views.", "OK");
                return;
            }

            await StartCameraAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Panorama capture start failed: {exception}");
        }
    }

    protected override void OnDisappearing()
    {
        isOnScreen = false;
        viewModel.IsCameraReady = false;
        cameraStart?.Cancel();
        try
        {
            camera?.StopCameraPreview();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }

        ArPlatform.ExitArMode();
        base.OnDisappearing();
    }

    private async Task StartCameraAsync()
    {
        cameraStart?.Cancel();
        cameraStart = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = cameraStart.Token;

        if (camera is null)
        {
            // First time: the toolkit connects and starts the preview by itself once the view is on screen.
            camera = new CameraView();
            CameraHost.Content = camera;
        }
        else
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await camera.StartCameraPreview(token);
                    break;
                }
                catch (Exception) when (attempt < 2 && !token.IsCancellationRequested)
                {
                    await Task.Delay(700, token);
                }
            }
        }

        // Wait until the toolkit has found the cameras (its handler is connected), then allow capturing.
        try
        {
            while (!token.IsCancellationRequested && (camera.Handler is null || !camera.IsAvailable))
            {
                await Task.Delay(250, token);
            }

            // The toolkit connects the camera and starts the preview a moment after that; also lets auto-exposure settle.
            await Task.Delay(1500, token);
            viewModel.IsCameraReady = isOnScreen && camera.Handler is not null;
        }
        catch (OperationCanceledException)
        {
            // left the page, or the camera never became available
        }
    }

    private async Task<byte[]> CapturePhotoAsync(CancellationToken token)
    {
        if (camera is null)
        {
            throw new InvalidOperationException("The camera is not ready.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var stream = await camera.CaptureImage(timeout.Token);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, timeout.Token);
        return buffer.ToArray();
    }
}
