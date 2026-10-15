namespace BCOpendayApp.Services;

/// <summary>Reads text aloud using the device's text-to-speech voice.</summary>
public interface INarrationService
{
    /// <summary>Speaks <paramref name="text"/>, stopping anything already being spoken. Throws if the device has no voice.</summary>
    Task SpeakAsync(string text);

    void Stop();
}
