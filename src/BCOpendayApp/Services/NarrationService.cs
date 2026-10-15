using Microsoft.Maui.Media;

namespace BCOpendayApp.Services;

public sealed class NarrationService : INarrationService
{
    private CancellationTokenSource? current;

    public async Task SpeakAsync(string text)
    {
        Stop();

        using var source = new CancellationTokenSource();
        current = source;

        try
        {
            await TextToSpeech.Default.SpeakAsync(text, null, source.Token);
        }
        catch (OperationCanceledException)
        {
            // Stop() was called - expected.
        }
        finally
        {
            if (ReferenceEquals(current, source))
            {
                current = null;
            }
        }
    }

    public void Stop() => current?.Cancel();
}
