namespace Sifra.Vault.Auth;

public interface IAccessAuditLog
{
    void Record(string eventName, bool success);
}
