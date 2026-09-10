using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;

namespace Sifra.Vault.Autofill;

/// <summary>
/// Decides which credentials to offer for a page, and fills only with
/// explicit consent. Reuses CredentialService/VaultEncryptionService
/// unmodified — this is a thin decision layer, not a new storage/crypto
/// path. Domain matching is intentionally simple (exact host match, no
/// public-suffix-list heuristics) for this walking skeleton.
/// </summary>
public sealed class CredentialAutofillService
{
    private readonly CredentialService _credentials;
    private readonly AuditLogger? _auditLogger;

    public CredentialAutofillService(CredentialService credentials, AuditLogger? auditLogger = null)
    {
        _credentials = credentials;
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// Discovery only — never fills anything. Returns credentials whose
    /// stored URL's host exactly matches the requested page's host.
    /// </summary>
    public IReadOnlyList<CredentialView> OfferCredentialsForUrl(string vaultCredential, string url)
    {
        var host = GetHost(url);
        var matches = _credentials.List(vaultCredential)
            .Where(c => c.Url is not null && string.Equals(GetHost(c.Url), host, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _auditLogger?.Log(nameof(OfferCredentialsForUrl), Environment.UserName, details: $"host={host} offered={matches.Count}");
        return matches;
    }

    /// <summary>
    /// Fills a credential only when explicitly consented to, and only if
    /// it was actually one of the credentials offered for this exact URL —
    /// a caller cannot fill an unrelated domain's credential just by
    /// passing its id.
    /// </summary>
    /// <exception cref="AutofillConsentRequiredException">consentGranted was false.</exception>
    /// <exception cref="CredentialDomainMismatchException">The credential does not belong to this URL's domain.</exception>
    public CredentialView FillCredential(string vaultCredential, string credentialId, string url, bool consentGranted)
    {
        if (!consentGranted)
        {
            _auditLogger?.Log(nameof(FillCredential), Environment.UserName, details: $"id={credentialId} outcome=consent_denied");
            throw new AutofillConsentRequiredException();
        }

        var offered = OfferCredentialsForUrlWithoutLogging(vaultCredential, url);
        if (!offered.Any(c => c.Id == credentialId))
        {
            _auditLogger?.Log(nameof(FillCredential), Environment.UserName, details: $"id={credentialId} outcome=domain_mismatch");
            throw new CredentialDomainMismatchException(credentialId, url);
        }

        var view = _credentials.GetById(vaultCredential, credentialId);
        _auditLogger?.Log(nameof(FillCredential), Environment.UserName, details: $"id={credentialId} outcome=filled");
        return view;
    }

    private IReadOnlyList<CredentialView> OfferCredentialsForUrlWithoutLogging(string vaultCredential, string url)
    {
        var host = GetHost(url);
        return _credentials.List(vaultCredential)
            .Where(c => c.Url is not null && string.Equals(GetHost(c.Url), host, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string GetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
}
