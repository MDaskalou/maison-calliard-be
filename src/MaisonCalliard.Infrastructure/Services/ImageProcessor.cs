using System.Diagnostics;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed record ProcessedImage(
    byte[] Bytes,
    int Width,
    int Height,
    long OriginalBytes,
    string OriginalFormat,
    int Quality,
    TimeSpan Duration);

internal sealed class ImageProcessor
{
    private static readonly HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPEG", "PNG", "WEBP"
    };

    private readonly ImageUploadOptions _options;
    private readonly ILogger<ImageProcessor> _logger;

    public ImageProcessor(ImageUploadOptions options, ILogger<ImageProcessor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<ProcessedImage> ProcessAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var input = new MemoryStream();
        await CopyWithLimitAsync(source, input, _options.MaxImageBytes, cancellationToken);
        var originalBytes = input.Length;
        input.Position = 0;

        IImageFormat format;
        ImageInfo info;
        try
        {
            format = await Image.DetectFormatAsync(input, cancellationToken)
                ?? throw new ImageValidationException("Filen är inte en giltig bild.");
            input.Position = 0;
            info = await Image.IdentifyAsync(input, cancellationToken)
                ?? throw new ImageValidationException("Bildens dimensioner kunde inte läsas.");
        }
        catch (ImageValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new ImageValidationException("Filen är inte en giltig JPEG-, PNG- eller WebP-bild.");
        }

        if (!AllowedFormats.Contains(format.Name))
        {
            throw new ImageValidationException($"Bildformatet {format.Name} stöds inte. Tillåtna format är JPEG, PNG och WebP.");
        }

        var pixels = (long)info.Width * info.Height;
        if (info.Width > _options.MaxInputWidth || info.Height > _options.MaxInputHeight || pixels > _options.MaxInputPixels)
        {
            throw new ImageValidationException(
                $"Bilden är för stor. Maximalt {_options.MaxInputWidth}×{_options.MaxInputHeight} px och {_options.MaxInputPixels:N0} pixlar tillåts.");
        }

        input.Position = 0;
        try
        {
            using var image = await Image.LoadAsync<Rgba32>(input, cancellationToken);
            image.Mutate(context =>
            {
                context.AutoOrient();
                var longSide = Math.Max(image.Width, image.Height);
                if (longSide > _options.MaxOutputLongSide)
                {
                    var scale = (double)_options.MaxOutputLongSide / longSide;
                    context.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(
                            Math.Max(1, (int)Math.Round(image.Width * scale)),
                            Math.Max(1, (int)Math.Round(image.Height * scale))),
                        Sampler = KnownResamplers.Lanczos3
                    });
                }
            });

            byte[] encoded = [];
            var usedQuality = _options.InitialWebPQuality;
            for (var quality = _options.InitialWebPQuality; quality >= _options.MinimumWebPQuality; quality -= 6)
            {
                await using var output = new MemoryStream();
                await image.SaveAsWebpAsync(output, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = quality
                }, cancellationToken);
                encoded = output.ToArray();
                usedQuality = quality;
                if (encoded.Length <= _options.TargetBytes)
                {
                    break;
                }
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Image optimized: {OriginalBytes} bytes {OriginalFormat} {OriginalWidth}x{OriginalHeight} -> {FinalBytes} bytes WebP {Width}x{Height}, quality {Quality}, {ElapsedMs} ms",
                originalBytes, format.Name, info.Width, info.Height, encoded.Length, image.Width, image.Height, usedQuality, stopwatch.ElapsedMilliseconds);

            if (encoded.Length > _options.TargetBytes)
            {
                _logger.LogWarning(
                    "Optimized image is {FinalBytes} bytes, above target {TargetBytes} bytes at minimum acceptable quality {Quality}",
                    encoded.Length, _options.TargetBytes, usedQuality);
            }

            return new ProcessedImage(encoded, image.Width, image.Height, originalBytes, format.Name, usedQuality, stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new ImageValidationException("Bildfilen är skadad eller innehåller data som inte kan avkodas.");
        }
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new ImageValidationException($"Bildfilen överskrider maxstorleken {maxBytes / 1024 / 1024} MB.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
