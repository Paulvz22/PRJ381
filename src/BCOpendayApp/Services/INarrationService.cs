namespace BCOpendayApp.Services;

/// <summary>Reads text aloud using the device's text-to-speech voice.</summary>
public interface INarrationService
{
    /// <summary>True while something is being read aloud.</summary>
    bool IsSpeaking { get; }

    /// <summary>Raised (on any thread) when <see cref="IsSpeaking"/> changes.</summary>
    event EventHandler? SpeakingChanged;

    /// <summary>Speaks <paramref name="text"/>, stopping anything already being spoken. Throws if the device has no voice.</summary>
    Task SpeakAsync(string text);

    void Stop();
}

/// <summary>When the AR "Listen" button is offered. Pure - unit-tested.</summary>
public static class NarrationRange
{
    /// <summary>The button appears within this distance of the building's nearest wall.</summary>
    public const double ShowWithinMeters = 15;

    /// <summary>
    /// Once shown it stays until the visitor is this far away. Phone GPS wanders by 3-10 m, so a single 15 m limit
    /// would make the button flicker on and off while the visitor stands still near it.
    /// </summary>
    public const double HideBeyondMeters = 20;

    public static bool IsInRange(double distanceMeters, bool wasInRange) =>
        double.IsFinite(distanceMeters) && distanceMeters <= (wasInRange ? HideBeyondMeters : ShowWithinMeters);
}
