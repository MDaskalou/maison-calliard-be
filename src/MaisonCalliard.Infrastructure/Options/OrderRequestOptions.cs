namespace MaisonCalliard.Infrastructure.Options;

public sealed class OrderRequestOptions
{
    public const string SectionName = "OrderRequest";

    public string ToEmail { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
}
