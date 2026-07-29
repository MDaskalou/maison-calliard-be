using FluentAssertions;
using MaisonCalliard.Infrastructure.Services;

namespace MaisonCalliard.UnitTests;

public sealed class SupabaseStorageServiceTests
{
    [Theory]
    [InlineData("https://frcoybsquqfwnyofumsg.supabase.co/storage/v1/object/public/uploads/abc123.webp", "abc123.webp")]
    [InlineData("https://project.supabase.co/storage/v1/object/public/uploads/nested/abc123.webp", "nested/abc123.webp")]
    public void TryGetObjectName_SupabasePublicUrl_ReturnsObjectName(string fileUrl, string expected)
    {
        SupabaseStorageService.TryGetObjectName(fileUrl).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://maison-calliard-be.azurewebsites.net/uploads/abc123.webp")]
    [InlineData("https://example.test/uploads/legacy-file.webp")]
    [InlineData("/uploads/legacy-file.webp")]
    public void TryGetObjectName_NonSupabaseUrl_ReturnsNull(string fileUrl)
    {
        SupabaseStorageService.TryGetObjectName(fileUrl).Should().BeNull();
    }
}
