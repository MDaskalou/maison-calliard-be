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
    public string Phone { get; set; } = string.Empty;
    public string OrderNotificationEmail { get; set; } = string.Empty;
}
