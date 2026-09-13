using Sifra.Vault.Credentials;

namespace Sifra.Vault.Tests;

/// <summary>
/// Shared helpers for tests written against the pre-dynamic-field-model API
/// shape (Add(label, username, password, url, ...)). The real model is now
/// a dynamic list of typed fields; these helpers keep test call sites
/// readable without every test re-deriving the same Login/Password/Website
/// field triple.
/// </summary>
internal static class CredentialFieldTestHelpers
{
    public static IReadOnlyList<(string Name, string Value, CustomFieldType Type)> LoginFields(
        string username, string password, string? url = null)
    {
        var fields = new List<(string, string, CustomFieldType)>
        {
            ("Username", username, CustomFieldType.Login),
            ("Password", password, CustomFieldType.Password),
        };
        if (url is not null)
        {
            fields.Add(("Website", url, CustomFieldType.Website));
        }
        return fields;
    }

    public static string Username(this CredentialView view) =>
        view.Fields.First(f => f.Type == CustomFieldType.Login).Value;

    public static string? UsernameOrNull(this CredentialView view) =>
        view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Login)?.Value;

    public static string Password(this CredentialView view) =>
        view.Fields.First(f => f.Type == CustomFieldType.Password).Value;

    public static string? PasswordOrNull(this CredentialView view) =>
        view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Password)?.Value;

    public static string? Url(this CredentialView view) =>
        view.Fields.FirstOrDefault(f => f.Type == CustomFieldType.Website)?.Value;
}
