using FluentAssertions;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Abstractions;

namespace MaisonCalliard.UnitTests;

public sealed class ImageProcessorTests
{
    private readonly ITestOutputHelper _output;

    public ImageProcessorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ProcessAsync_Jpeg_ResizesWithoutUpscalingAndProducesWebPBelowTarget()
    {
        using var image = new Image<Rgba32>(3000, 1000, new Rgba32(180, 90, 50));
        await using var input = new MemoryStream();
        await image.SaveAsJpegAsync(input, new JpegEncoder { Quality = 95 });
        input.Position = 0;

        var result = await CreateProcessor().ProcessAsync(input);

        result.OriginalFormat.Should().Be("JPEG");
        result.Width.Should().Be(2400);
        result.Height.Should().Be(800);
        result.Bytes.Should().HaveCountLessThanOrEqualTo(200 * 1024);
        (await Image.DetectFormatAsync(new MemoryStream(result.Bytes))).Should().BeOfType<WebpFormat>();
    }

    [Fact]
    public async Task ProcessAsync_Png_PreservesTransparency()
    {
        using var image = new Image<Rgba32>(100, 50, new Rgba32(20, 30, 40, 0));
        image[25, 25] = new Rgba32(200, 100, 50, 255);
        await using var input = new MemoryStream();
        await image.SaveAsPngAsync(input, new PngEncoder());
        input.Position = 0;

        var result = await CreateProcessor().ProcessAsync(input);
        using var output = Image.Load<Rgba32>(result.Bytes);

        result.OriginalFormat.Should().Be("PNG");
        output[0, 0].A.Should().Be(0);
        output[25, 25].A.Should().Be(255);
    }

    [Fact]
    public async Task ProcessAsync_ExifOrientation_RotatesPixelsAndDimensions()
    {
        using var image = new Image<Rgba32>(120, 60, new Rgba32(80, 120, 160));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        await using var input = new MemoryStream();
        await image.SaveAsJpegAsync(input);
        input.Position = 0;

        var result = await CreateProcessor().ProcessAsync(input);

        result.Width.Should().Be(60);
        result.Height.Should().Be(120);
    }

    [Fact]
    public async Task ProcessAsync_ImageExceedingDimensionLimit_IsRejectedBeforeDecode()
    {
        using var image = new Image<Rgba32>(12_001, 1);
        await using var input = new MemoryStream();
        await image.SaveAsPngAsync(input);
        input.Position = 0;
        var processor = CreateProcessor();

        var act = () => processor.ProcessAsync(input);

        await act.Should().ThrowAsync<ImageValidationException>().WithMessage("*för stor*");
    }

    [Fact]
    public async Task ProcessAsync_FileExceedingByteLimit_IsRejected()
    {
        var processor = CreateProcessor(new ImageUploadOptions { MaxImageBytes = 4 });
        await using var input = new MemoryStream(new byte[5]);

        var act = () => processor.ProcessAsync(input);

        await act.Should().ThrowAsync<ImageValidationException>().WithMessage("*maxstorleken*");
    }

    [Fact]
    public async Task ProcessAsync_InvalidFile_IsRejectedRegardlessOfClaimedExtensionOrMimeType()
    {
        var processor = CreateProcessor();
        await using var input = new MemoryStream("not an image"u8.ToArray());

        var act = () => processor.ProcessAsync(input);

        await act.Should().ThrowAsync<ImageValidationException>();
    }

    [Fact]
    public async Task SaveAsync_UsesDetectedContentEvenWhenFileNameAndMimeTypeAreWrong()
    {
        var uploadPath = Path.Combine(Path.GetTempPath(), $"maison-calliard-images-{Guid.NewGuid():N}");
        try
        {
            using var image = new Image<Rgba32>(20, 10, new Rgba32(20, 40, 60));
            await using var input = new MemoryStream();
            await image.SaveAsPngAsync(input);
            input.Position = 0;
            var storage = new LocalFileStorageService(uploadPath, "https://example.test", CreateProcessor());

            var url = await storage.SaveAsync(input, "not-an-image.txt", "text/plain");

            url.Should().EndWith(".webp");
            var savedPath = Path.Combine(uploadPath, Path.GetFileName(new Uri(url).LocalPath));
            (await Image.DetectFormatAsync(savedPath)).Should().BeOfType<WebpFormat>();
        }
        finally
        {
            if (Directory.Exists(uploadPath))
            {
                Directory.Delete(uploadPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessAsync_ExistingRepositoryUploads_ReducesEveryImageBelowTarget()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var uploadPath = Path.Combine(repositoryRoot, "src", "MaisonCalliard.Api", "wwwroot", "uploads");
        var files = Directory.GetFiles(uploadPath)
            .Where(path => new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToList();
        files.Should().NotBeEmpty();
        var processor = CreateProcessor();

        foreach (var file in files)
        {
            await using var input = File.OpenRead(file);
            var result = await processor.ProcessAsync(input);
            _output.WriteLine($"{Path.GetFileName(file)}: {result.OriginalBytes} -> {result.Bytes.Length} bytes, {result.Width}x{result.Height}, q={result.Quality}");
            result.Bytes.Should().HaveCountLessThanOrEqualTo(200 * 1024, Path.GetFileName(file));
        }
    }

    private static ImageProcessor CreateProcessor(ImageUploadOptions? options = null) =>
        new(options ?? new ImageUploadOptions(), NullLogger<ImageProcessor>.Instance);
}
