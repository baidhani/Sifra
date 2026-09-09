namespace Sifra.Vault.Audit;

/// <summary>Thrown when a unique operation id could not be generated — the "operation ID collision" failure path.</summary>
public sealed class OperationIdCollisionException : Exception
{
    public OperationIdCollisionException(string message)
        : base(message)
    {
    }
}
