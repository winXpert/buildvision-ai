using BuildVision.Api.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BuildVision.Api.Services;

public interface IImageProcessingService
{
    Task<(string OriginalPath, string MaskPath)> SaveUploadAndMaskAsync(
        IFormFile image,
        SelectionBox? selection,
        IFormFile? maskFile,
        Guid jobId,
        CancellationToken ct = default);

    Task<string> CreateDemoEditAsync(
        string originalPath,
        string maskPath,
        string prompt,
        Guid jobId,
        int variationIndex,
        CancellationToken ct = default);
}

public sealed class ImageProcessingService : IImageProcessingService
{
    private readonly string _uploadsRoot;
    private readonly string _resultsRoot;

    public ImageProcessingService(IWebHostEnvironment env)
    {
        _uploadsRoot = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
        _resultsRoot = Path.Combine(env.ContentRootPath, "App_Data", "results");
        Directory.CreateDirectory(_uploadsRoot);
        Directory.CreateDirectory(_resultsRoot);
    }

    public async Task<(string OriginalPath, string MaskPath)> SaveUploadAndMaskAsync(
        IFormFile image,
        SelectionBox? selection,
        IFormFile? maskFile,
        Guid jobId,
        CancellationToken ct = default)
    {
        var jobDir = Path.Combine(_uploadsRoot, jobId.ToString("N"));
        Directory.CreateDirectory(jobDir);

        var originalPath = Path.Combine(jobDir, "original.png");
        await using var input = image.OpenReadStream();
        using var img = await Image.LoadAsync<Rgba32>(input, ct);
        await img.SaveAsPngAsync(originalPath, ct);

        var maskPath = Path.Combine(jobDir, "mask.png");
        if (maskFile is not null)
        {
            await using var maskStream = maskFile.OpenReadStream();
            using var maskImg = await Image.LoadAsync<Rgba32>(maskStream, ct);
            if (maskImg.Width != img.Width || maskImg.Height != img.Height)
            {
                maskImg.Mutate(x => x.Resize(img.Width, img.Height));
            }

            await maskImg.SaveAsPngAsync(maskPath, ct);
        }
        else
        {
            using var mask = BuildMaskFromSelection(img.Width, img.Height, selection);
            await mask.SaveAsPngAsync(maskPath, ct);
        }

        return (originalPath, maskPath);
    }

    public async Task<string> CreateDemoEditAsync(
        string originalPath,
        string maskPath,
        string prompt,
        Guid jobId,
        int variationIndex,
        CancellationToken ct = default)
    {
        _ = prompt;
        var resultDir = Path.Combine(_resultsRoot, jobId.ToString("N"));
        Directory.CreateDirectory(resultDir);
        var resultPath = Path.Combine(resultDir, $"design-{variationIndex + 1}.png");

        using var original = await Image.LoadAsync<Rgba32>(originalPath, ct);
        using var mask = await Image.LoadAsync<Rgba32>(maskPath, ct);

        if (mask.Width != original.Width || mask.Height != original.Height)
        {
            mask.Mutate(x => x.Resize(original.Width, original.Height));
        }

        var accents = new (byte R, byte G, byte B)[]
        {
            (31, 111, 91),
            (196, 92, 38),
            (43, 76, 126),
            (107, 79, 58)
        };
        var (tr, tg, tb) = accents[variationIndex % accents.Length];

        for (var y = 0; y < original.Height; y++)
        {
            for (var x = 0; x < original.Width; x++)
            {
                if (mask[x, y].A >= 128)
                {
                    continue;
                }

                var pixel = original[x, y];
                original[x, y] = new Rgba32(
                    (byte)((pixel.R * 0.45) + (tr * 0.55)),
                    (byte)((pixel.G * 0.45) + (tg * 0.55)),
                    (byte)((pixel.B * 0.45) + (tb * 0.55)),
                    255);
            }
        }

        // Stripe banner marking demo mode
        var bannerHeight = Math.Max(18, original.Height / 28);
        for (var y = 0; y < bannerHeight; y++)
        {
            for (var x = 0; x < original.Width; x++)
            {
                var pixel = original[x, y];
                original[x, y] = new Rgba32(
                    (byte)((pixel.R + 20) / 2),
                    (byte)((pixel.G + 20) / 2),
                    (byte)((pixel.B + 20) / 2),
                    255);
            }
        }

        await original.SaveAsPngAsync(resultPath, ct);
        return resultPath;
    }

    private static Image<Rgba32> BuildMaskFromSelection(int width, int height, SelectionBox? selection)
    {
        // Opaque = keep, transparent = edit (OpenAI images/edits convention)
        var mask = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 255));

        if (selection is null || selection.Width <= 0 || selection.Height <= 0)
        {
            var top = Math.Max(1, height / 3);
            for (var y = 0; y < top; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    mask[x, y] = new Rgba32(0, 0, 0, 0);
                }
            }

            return mask;
        }

        var scaleX = width / Math.Max(1.0, selection.ImageWidth);
        var scaleY = height / Math.Max(1.0, selection.ImageHeight);
        var x0 = (int)Math.Clamp(Math.Round(selection.X * scaleX), 0, width - 1);
        var y0 = (int)Math.Clamp(Math.Round(selection.Y * scaleY), 0, height - 1);
        var x1 = (int)Math.Clamp(Math.Round((selection.X + selection.Width) * scaleX), x0 + 1, width);
        var y1 = (int)Math.Clamp(Math.Round((selection.Y + selection.Height) * scaleY), y0 + 1, height);

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                mask[x, y] = new Rgba32(0, 0, 0, 0);
            }
        }

        return mask;
    }
}
