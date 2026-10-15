using System.Diagnostics;
#if FIREBASE_CONFIGURED
using Plugin.Firebase.Auth;
#endif

namespace BCOpendayApp.Services;

public class FirebaseAuthService : IAuthService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    // Visitors do not need to create an account. Enable Anonymous Auth in Firebase first.
    public async Task<bool> EnsureSignedInAsync()
    {
        if (!FirebaseAvailability.IsReady) return false;
        await gate.WaitAsync();
        try
        {
#if FIREBASE_CONFIGURED
            if (CrossFirebaseAuth.Current.CurrentUser is not null) return true;
            return await CrossFirebaseAuth.Current.SignInAnonymouslyAsync() is not null;
#else
            return false;
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Firebase anonymous sign-in failed: {ex.Message}");
            return false;
        }
        finally { gate.Release(); }
    }

    public string? CurrentUserId
    {
        get
        {
#if FIREBASE_CONFIGURED
            if (!FirebaseAvailability.IsReady) return null;
            try { return CrossFirebaseAuth.Current.CurrentUser?.Uid; }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
#endif
            return null;
        }
    }
}
