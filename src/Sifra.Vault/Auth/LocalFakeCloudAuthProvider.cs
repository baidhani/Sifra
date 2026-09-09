namespace Sifra.Vault.Auth;

/// <summary>
/// Stand-in cloud-storage authenticator with no network calls — this repo
/// has no real cloud provider integration yet (that is STORY-007). It
/// exists only to prove, in tests, that vault authentication behaves
/// identically no matter what state a cloud provider is in.
/// </summary>
public sealed class LocalFakeCloudAuthProvider : ICloudAuthProvider
{
    /// <summary>When true, the next SignIn call throws, simulating a failed cloud login.</summary>
    public bool SimulateFailure { get; set; }

    public bool IsAuthenticated { get; private set; }

    public void SignIn(string accountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(accountIdentifier))
        {
            throw new ArgumentException("Account identifier is required.", nameof(accountIdentifier));
        }

        if (SimulateFailure)
        {
            throw new CloudAuthenticationException($"Cloud sign-in failed for '{accountIdentifier}'.");
        }

        IsAuthenticated = true;
    }

    public void SignOut()
    {
        IsAuthenticated = false;
    }
}
