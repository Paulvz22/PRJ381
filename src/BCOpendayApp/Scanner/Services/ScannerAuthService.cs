using System.Diagnostics;
using BCOpendayApp.Services;
#if FIREBASE_CONFIGURED
using Plugin.Firebase.Auth;
using Plugin.Firebase.Firestore;
#endif

namespace BCOpendayApp.Scanner.Services;

/// <summary>
/// The scanner's "password": a Firebase Authentication email + password account that an admin has listed in
/// the Firestore collection "scanners" (document id = the account's uid). The check happens on Google's servers
/// and Firestore rules only accept building writes from these accounts, so the password cannot be bypassed by
/// modifying the app - unlike a password compared inside the app.
/// </summary>
public interface IScannerAuthService
{
    /// <summary>True after a successful sign-in in this app session (expires after <see cref="SessionLength"/>).</summary>
    bool IsUnlocked { get; }

    string StaffEmail { get; }

    string? StaffUid { get; }

    /// <summary>The last email used on this phone (never the password).</summary>
    string RememberedEmail { get; }

    /// <summary>Returns null on success, otherwise a message to show.</summary>
    Task<string?> SignInAsync(string email, string password);

    /// <summary>Signs the staff account out and goes back to the anonymous visitor account.</summary>
    Task SignOutAsync();
}

public sealed class ScannerAuthService : IScannerAuthService
{
    public const string ScannersCollection = "scanners";
    public static readonly TimeSpan SessionLength = TimeSpan.FromMinutes(45);

    private const string EmailKey = "scanner_email";
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutTime = TimeSpan.FromSeconds(30);

    private readonly IAuthService visitorAuth;
    private DateTimeOffset unlockedAt;
    private int failedAttempts;
    private DateTimeOffset lockedUntil;
    private bool unlocked;

    public ScannerAuthService(IAuthService visitorAuth) => this.visitorAuth = visitorAuth;

    public bool IsUnlocked => unlocked && DateTimeOffset.UtcNow - unlockedAt < SessionLength;

    public string StaffEmail { get; private set; } = string.Empty;

    public string? StaffUid { get; private set; }

    public string RememberedEmail => Preferences.Default.Get(EmailKey, string.Empty);

    public async Task<string?> SignInAsync(string email, string password)
    {
        email = (email ?? string.Empty).Trim();
        if (email.Length == 0 || string.IsNullOrEmpty(password))
        {
            return "Enter the staff email and password.";
        }

        if (DateTimeOffset.UtcNow < lockedUntil)
        {
            return $"Too many attempts. Try again in {Math.Ceiling((lockedUntil - DateTimeOffset.UtcNow).TotalSeconds)} seconds.";
        }

        if (!FirebaseAvailability.IsReady)
        {
            return "The scanner needs Firebase. Add Platforms/Android/google-services.json and rebuild the app.";
        }

        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return "The scanner needs an internet connection to sign in.";
        }

#if FIREBASE_CONFIGURED
        try
        {
            // createsUserAutomatically MUST be false: Plugin.Firebase defaults to creating an account for any
            // unknown email, which would let anyone register themselves from the login screen.
            var user = await CrossFirebaseAuth.Current.SignInWithEmailAndPasswordAsync(email, password, createsUserAutomatically: false);
            var uid = user?.Uid;
            if (string.IsNullOrEmpty(uid))
            {
                return Failed("Sign-in failed.");
            }

            // The account exists - is it allowed to scan?
            var snapshot = await CrossFirebaseFirestore.Current
                .GetCollection(ScannersCollection)
                .GetDocument(uid)
                .GetDocumentSnapshotAsync<ScannerDocument>(Source.Server);

            if (snapshot?.Data is null)
            {
                await SignOutCoreAsync();
                return Failed($"This account is not allowed to use the scanner. An admin must add a document with id '{uid}' to the '{ScannersCollection}' collection.");
            }

            failedAttempts = 0;
            unlocked = true;
            unlockedAt = DateTimeOffset.UtcNow;
            StaffEmail = email;
            StaffUid = uid;
            Preferences.Default.Set(EmailKey, email);
            return null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Scanner sign-in failed: {exception}");
            await SignOutCoreAsync();
            return Failed("Wrong email or password, or the Email/Password sign-in method is not enabled in Firebase.");
        }
#else
        await Task.CompletedTask;
        return "The scanner needs Firebase. Add Platforms/Android/google-services.json and rebuild the app.";
#endif
    }

    public async Task SignOutAsync()
    {
        await SignOutCoreAsync();
        try
        {
            await visitorAuth.EnsureSignedInAsync(); // back to the anonymous visitor account
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private async Task SignOutCoreAsync()
    {
        unlocked = false;
        StaffUid = null;
        StaffEmail = string.Empty;
#if FIREBASE_CONFIGURED
        try
        {
            if (FirebaseAvailability.IsReady)
            {
                await CrossFirebaseAuth.Current.SignOutAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Sign-out failed: {exception.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    private string Failed(string message)
    {
        if (++failedAttempts >= MaxAttempts)
        {
            failedAttempts = 0;
            lockedUntil = DateTimeOffset.UtcNow + LockoutTime;
        }

        return message;
    }
}

#if FIREBASE_CONFIGURED
/// <summary>scanners/{uid} - the admin adds one per staff account (field "name" so it is easy to recognise).</summary>
public sealed class ScannerDocument : IFirestoreObject
{
    [FirestoreProperty("name")]
    public string Name { get; set; } = "";
}
#endif
