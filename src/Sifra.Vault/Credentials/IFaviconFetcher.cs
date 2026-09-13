namespace Sifra.Vault.Credentials;

/// <summary>Fetches a website's favicon image bytes. Returns null on any failure — a missing icon is never fatal.</summary>
public interface IFaviconFetcher
{
    Task<byte[]?> FetchAsync(string websiteUrl, CancellationToken cancellationToken);
}
