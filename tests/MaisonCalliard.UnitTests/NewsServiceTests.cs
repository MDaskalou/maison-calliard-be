using FluentAssertions;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Application.News;
using MaisonCalliard.Application.News.Dtos;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Domain.ValueObjects;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class NewsServiceTests
{
    private readonly Mock<INewsRepository> _newsRepositoryMock = new();
    private readonly Mock<IFileStorageService> _fileStorageMock = new();
    private readonly NewsService _sut;

    public NewsServiceTests()
    {
        _sut = new NewsService(_newsRepositoryMock.Object, _fileStorageMock.Object);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllNewsItems()
    {
        // Arrange
        var items = new List<NewsItem>
        {
            new() { Id = Guid.NewGuid(), Title = new LocalizedText { Se = "Titel", En = "Title" }, Subtitle = new LocalizedText { Se = "Sub", En = "Sub" }, ImageUrl = "https://example.com/img.jpg" }
        };
        _newsRepositoryMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(items);

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Se.Should().Be("Titel");
    }

    [Fact]
    public async Task CreateAsync_WithoutImage_CreatesNewsItemWithEmptyImageUrl()
    {
        // Arrange
        var request = new CreateNewsItemRequest
        {
            TitleSe = "Nyhet",
            TitleEn = "News",
            SubtitleSe = "Sub Se",
            SubtitleEn = "Sub En"
        };

        // Act
        var result = await _sut.CreateAsync(request, null, null, null);

        // Assert
        _newsRepositoryMock.Verify(r => r.AddAsync(It.IsAny<NewsItem>(), default), Times.Once);
        result.Title.Se.Should().Be("Nyhet");
        result.ImageUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithImage_SavesImageAndSetsUrl()
    {
        // Arrange
        var request = new CreateNewsItemRequest { TitleSe = "Nyhet", TitleEn = "News", SubtitleSe = "Sub", SubtitleEn = "Sub" };
        var imageStream = new MemoryStream([1, 2, 3]);
        _fileStorageMock.Setup(f => f.SaveAsync(imageStream, "image.jpg", "image/jpeg", default))
            .ReturnsAsync("https://example.com/uploads/image.jpg");

        // Act
        var result = await _sut.CreateAsync(request, imageStream, "image.jpg", "image/jpeg");

        // Assert
        result.ImageUrl.Should().Be("https://example.com/uploads/image.jpg");
    }

    [Fact]
    public async Task CreateAsync_WhenDatabaseWriteFails_RemovesNewImage()
    {
        var request = new CreateNewsItemRequest { TitleSe = "Nyhet", TitleEn = "News", SubtitleSe = "Sub", SubtitleEn = "Sub" };
        var imageStream = new MemoryStream([1, 2, 3]);
        const string newUrl = "https://example.com/uploads/new.webp";
        _fileStorageMock.Setup(f => f.SaveAsync(imageStream, "image.jpg", "image/jpeg", default)).ReturnsAsync(newUrl);
        _newsRepositoryMock.Setup(r => r.AddAsync(It.IsAny<NewsItem>(), default)).ThrowsAsync(new InvalidOperationException());

        var act = () => _sut.CreateAsync(request, imageStream, "image.jpg", "image/jpeg");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _fileStorageMock.Verify(f => f.DeleteAsync(newUrl, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenDatabaseWriteFails_KeepsOldImageAndRemovesNewImage()
    {
        var id = Guid.NewGuid();
        const string oldUrl = "https://example.com/uploads/old.jpg";
        const string newUrl = "https://example.com/uploads/new.webp";
        var item = new NewsItem { Id = id, Title = new LocalizedText(), Subtitle = new LocalizedText(), ImageUrl = oldUrl };
        var request = new UpdateNewsItemRequest { TitleSe = "Ny", TitleEn = "New", SubtitleSe = "Sub", SubtitleEn = "Sub" };
        var imageStream = new MemoryStream([1, 2, 3]);
        _newsRepositoryMock.Setup(r => r.GetByIdAsync(id, default)).ReturnsAsync(item);
        _fileStorageMock.Setup(f => f.SaveAsync(imageStream, "image.jpg", "image/jpeg", default)).ReturnsAsync(newUrl);
        _newsRepositoryMock.Setup(r => r.UpdateAsync(item, default)).ThrowsAsync(new InvalidOperationException());

        var act = () => _sut.UpdateAsync(id, request, imageStream, "image.jpg", "image/jpeg");

        await act.Should().ThrowAsync<InvalidOperationException>();
        item.ImageUrl.Should().Be(oldUrl);
        _fileStorageMock.Verify(f => f.DeleteAsync(newUrl, CancellationToken.None), Times.Once);
        _fileStorageMock.Verify(f => f.DeleteAsync(oldUrl, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _newsRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((NewsItem?)null);

        // Act
        var act = async () => await _sut.DeleteAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_WhenFound_DeletesAndCleansUpImage()
    {
        // Arrange
        var id = Guid.NewGuid();
        var item = new NewsItem { Id = id, Title = new LocalizedText(), Subtitle = new LocalizedText(), ImageUrl = "https://example.com/old.jpg" };
        _newsRepositoryMock.Setup(r => r.GetByIdAsync(id, default)).ReturnsAsync(item);

        // Act
        await _sut.DeleteAsync(id);

        // Assert
        _fileStorageMock.Verify(f => f.DeleteAsync("https://example.com/old.jpg", default), Times.Once);
        _newsRepositoryMock.Verify(r => r.DeleteAsync(item, default), Times.Once);
    }
}
