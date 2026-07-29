namespace MaisonCalliard.Infrastructure.Options;

public sealed class SupabaseStorageOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string StorageBucket { get; set; } = "uploads";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) &&
        !string.IsNullOrWhiteSpace(ServiceRoleKey) &&
        !string.IsNullOrWhiteSpace(StorageBucket);
}
