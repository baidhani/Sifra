namespace Sifra.Vault.Sharing;

public sealed class ShareNotFoundException : Exception
{
    public ShareNotFoundException(string shareId)
        : base($"No share found with id '{shareId}'.")
    {
    }
}
