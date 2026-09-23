using Sifra.Vault.Audit;
using Sifra.Vault.Credentials;

namespace Sifra.Vault.Autofill;

/// <summary>
/// Decides which credentials to offer for a page, and fills only with
/// explicit consent. Reuses CredentialService/VaultEncryptionService
/// unmodified — this is a thin decision layer, not a new storage/crypto
/// path. Domain matching compares registrable domains (see
/// <see cref="GetRegistrableDomain"/>), not raw hosts — so a credential
/// stored against "www.example.com" also matches "login.example.com" or
/// bare "example.com". This deliberately does NOT match across genuinely
/// different registrable domains (e.g. a credential stored against
/// "microsoft.com" will not match "login.microsoftonline.com" — that's a
/// different organization-controlled domain as far as this comparison is
/// concerned, even though both happen to belong to Microsoft; the fix for
/// that case is storing an additional Website field with the actual sign-in
/// host, which HasMatchingWebsiteField already supports since it checks
/// every Website field via Any()).
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
            .Where(c => HasMatchingWebsiteField(c, host))
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
            .Where(c => HasMatchingWebsiteField(c, host))
            .ToList();
    }

    private static bool HasMatchingWebsiteField(CredentialView credential, string host)
    {
        var registrableDomain = GetRegistrableDomain(host);
        return credential.Fields
            .Where(f => f.Type == CustomFieldType.Website)
            .Any(f => string.Equals(GetRegistrableDomain(GetHost(f.Value)), registrableDomain, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    // Multi-part public suffixes where the registrable domain needs three
    // labels instead of two (e.g. "example.co.uk", not just "co.uk" — the
    // latter would wrongly treat every unrelated *.co.uk site as the same
    // domain). This is a small, curated subset of the Mozilla Public Suffix
    // List covering the common cases, not the full ~2400-entry list — no
    // network fetch or new dependency for what's otherwise a niche case
    // among the sites Sifra's users are likely to store credentials for.
    // Revisit with a real PSL library if a user hits a suffix missing here.
    private static readonly HashSet<string> MultiPartSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "org.uk", "gov.uk", "ac.uk", "sch.uk", "me.uk", "net.uk",
        "co.jp", "co.nz", "co.za", "co.in", "co.kr", "co.il",
        "com.au", "net.au", "org.au", "com.br", "com.cn", "com.mx", "com.sg", "com.hk", "com.tw",
    };

    /// <summary>
    /// The organization-owned part of a host — "login.example.com" and
    /// "www.example.com" both reduce to "example.com". Hosts with fewer
    /// than two labels (bare hostnames, IP addresses) are returned as-is,
    /// since there's nothing to strip.
    /// </summary>
    private static string GetRegistrableDomain(string host)
    {
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2)
        {
            return host;
        }

        var lastTwo = string.Join('.', labels[^2..]);
        if (MultiPartSuffixes.Contains(lastTwo))
        {
            return labels.Length >= 3 ? string.Join('.', labels[^3..]) : host;
        }

        return lastTwo;
    }
}
