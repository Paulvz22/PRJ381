using Microsoft.Maui.Media;

namespace BCOpendayApp.Services;

/// <summary>The phone's own text-to-speech voice: free, offline, and always reads the current description.</summary>
public sealed class NarrationService : INarrationService
{
    private CancellationTokenSource? current;

    public bool IsSpeaking => current is not null;

    public event EventHandler? SpeakingChanged;

    public async Task SpeakAsync(string text)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var source = new CancellationTokenSource();
        current = source;
        SpeakingChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await TextToSpeech.Default.SpeakAsync(text.Trim(), null, source.Token);
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
                SpeakingChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Stop()
    {
        var speaking = current;
        if (speaking is null)
        {
            return;
        }

        current = null;
        speaking.Cancel();
        SpeakingChanged?.Invoke(this, EventArgs.Empty);
    }
}
