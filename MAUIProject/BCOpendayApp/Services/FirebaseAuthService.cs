using Plugin.Firebase.Auth;

namespace BCOpendayApp.Services;

public class FirebaseAuthService : IAuthService
{
    // Anonymous sign-in: Open Day visitors don't need accounts.
    public async Task<bool> EnsureSignedInAsync()
    {
        try
        {
            if (CrossFirebaseAuth.Current.CurrentUser is not null)
                return true;

            var user = await CrossFirebaseAuth.Current.SignInAnonymouslyAsync();
            return user is not null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Firebase anonymous sign-in failed: {ex.Message}");
            return false;
        }
    }

    public string? CurrentUserId => CrossFirebaseAuth.Current.CurrentUser?.Uid;
}
