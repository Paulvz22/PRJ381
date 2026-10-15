using System.Diagnostics;

namespace BCOpendayApp.Services;

public static class FirebaseAvailability
{
    public static bool IsReady { get; private set; }
    public static void MarkReady() => IsReady = true;
    public static void MarkUnavailable(Exception exception)
    {
        IsReady = false;
        Debug.WriteLine($"Firebase initialization unavailable: {exception.Message}");
    }
}
