using Plugin.Firebase.Auth;

namespace BCOpendayApp.Services
{
    public class FirebaseAuthService
    {
        // Signs the user in anonymously if they aren't already signed in.
        // Anonymous sign-in fits our use case: walk-up Open Day visitors 
        // don't need to create accounts, but Firebase still gives each 
        // session a stable, unique user ID under the hood.
        public async Task<bool> EnsureSignedInAsync()
        {
            try
            {
                var currentUser = CrossFirebaseAuth.Current.CurrentUser;

                if (currentUser is not null)
                {
                    // Already signed in from a previous app session — nothing to do.
                    return true;
                }

                var user = await CrossFirebaseAuth.Current.SignInAnonymouslyAsync();
                return user is not null;
            }
            catch (Exception ex)
            {
                // For now, just log it — later we might want to show 
                // the user a friendly error if sign-in genuinely fails.
                Console.WriteLine($"Firebase anonymous sign-in failed: {ex.Message}");
                return false;
            }
        }

        // Handy elsewhere in the app if you need to know who's "logged in"
        // (e.g. to tag Firestore writes with a user ID).
        public string? CurrentUserId => CrossFirebaseAuth.Current.CurrentUser?.Uid;
    }
}