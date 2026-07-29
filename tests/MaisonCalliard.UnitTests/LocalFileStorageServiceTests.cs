using FluentAssertions;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MaisonCalliard.UnitTests;

public sealed class LocalFileStorageServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    [InlineData("/uploads/legacy-file.webp")]
    [InlineData("uploads/legacy-file.webp")]
    public async Task DeleteAsync_InvalidOrRelativeUrls_DoNotThrow(string fileUrl)
    {
        var uploadPath = Path.Combine(Path.GetTempPath(), $"maison-calliard-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(uploadPath);
        try
        {
            var storage = CreateStorage(uploadPath);

            var act = () => storage.DeleteAsync(fileUrl);

            await act.Should().NotThrowAsync();
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
    public async Task DeleteAsync_AbsoluteUrl_DeletesLocalFile()
    {
        var uploadPath = Path.Combine(Path.GetTempPath(), $"maison-calliard-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(uploadPath);
        try
        {
            var fileName = $"{Guid.NewGuid():N}.webp";
            var filePath = Path.Combine(uploadPath, fileName);
            await File.WriteAllBytesAsync(filePath, [1, 2, 3]);
            var storage = CreateStorage(uploadPath);

            await storage.DeleteAsync($"https://example.test/uploads/{fileName}");

            File.Exists(filePath).Should().BeFalse();
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
    public async Task DeleteAsync_RelativeUploadPath_DeletesLocalFile()
    {
        var uploadPath = Path.Combine(Path.GetTempPath(), $"maison-calliard-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(uploadPath);
        try
        {
            var fileName = $"{Guid.NewGuid():N}.webp";
            var filePath = Path.Combine(uploadPath, fileName);
            await File.WriteAllBytesAsync(filePath, [1, 2, 3]);
            var storage = CreateStorage(uploadPath);

            await storage.DeleteAsync($"/uploads/{fileName}");

            File.Exists(filePath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(uploadPath))
            {
                Directory.Delete(uploadPath, recursive: true);
            }
        }
    }

    private static LocalFileStorageService CreateStorage(string uploadPath) =>
        new(uploadPath, "https://example.test", new ImageProcessor(new ImageUploadOptions(), NullLogger<ImageProcessor>.Instance));
}
