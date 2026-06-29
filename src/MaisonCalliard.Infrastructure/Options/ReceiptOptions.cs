namespace MaisonCalliard.Infrastructure.Options;

public sealed class ReceiptOptions
{
    public const string SectionName = "Receipt";

    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Maison Caillard";
    public string CompanyName { get; set; } = "Maison Caillard";
    public string SupportEmail { get; set; } = string.Empty;
    public string MolndalAddress { get; set; } = string.Empty;
    public string JarntorgetAddress { get; set; } = string.Empty;
    public string MolndalLegalName { get; set; } = "Maison Caillard AB";
    public string MolndalOrganizationNumber { get; set; } = "5591891295";
    public string MolndalVatNumber { get; set; } = "SE559189129501";
    public string JarntorgetLegalName { get; set; } = "Café Caillard AB";
    public string JarntorgetOrganizationNumber { get; set; } = "559570-1623";
    public string JarntorgetVatNumber { get; set; } = "SE556878500901";
    public string Phone { get; set; } = string.Empty;
    public string OrderNotificationEmail { get; set; } = string.Empty;
}
