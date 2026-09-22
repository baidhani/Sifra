namespace Sifra.Vault.Credentials;

/// <summary>File format for CredentialService.ExportByTag — matches the three formats offered by the "Export As" dialog.</summary>
public enum CredentialExportFormat
{
    PlainText,
    Csv,
    Xml,
}
