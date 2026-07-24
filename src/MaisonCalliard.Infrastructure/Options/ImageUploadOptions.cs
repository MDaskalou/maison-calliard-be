namespace MaisonCalliard.Infrastructure.Options;

internal sealed class ImageUploadOptions
{
    public const string SectionName = "Uploads";

    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxInputWidth { get; set; } = 12_000;
    public int MaxInputHeight { get; set; } = 12_000;
    public long MaxInputPixels { get; set; } = 80_000_000;
    public int MaxOutputLongSide { get; set; } = 2_400;
    public int TargetBytes { get; set; } = 200 * 1024;
    public int InitialWebPQuality { get; set; } = 82;
    public int MinimumWebPQuality { get; set; } = 46;
}
