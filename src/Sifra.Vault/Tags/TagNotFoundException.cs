namespace Sifra.Vault.Tags;

/// <summary>Thrown when an operation targets a tag id that does not exist.</summary>
public sealed class TagNotFoundException : Exception
{
    public TagNotFoundException(string id)
        : base($"No tag found with id '{id}'.")
    {
    }
}
