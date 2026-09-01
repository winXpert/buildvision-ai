using System.Net.Http.Headers;
using System.Text.Json;
using BuildVision.Api.Models;
using Microsoft.Extensions.Options;

namespace BuildVision.Api.Services;

public sealed record GenerationResult(IReadOnlyList<string> ImagePaths, bool UsedDemoMode);

public interface IImageGenerationService
{
    bool IsConfigured { get; }

    Task<GenerationResult> GenerateEditsAsync(
        string originalPath,
        string maskPath,
        string prompt,
        Guid jobId,
        int variations,
        CancellationToken ct = default);
}

public sealed class OpenAiImageGenerationService : IImageGenerationService
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly IImageProcessingService _imageProcessing;
    private readonly ILogger<OpenAiImageGenerationService> _logger;
    private readonly string _resultsRoot;

    public OpenAiImageGenerationService(
        HttpClient http,
        IOptions<OpenAiOptions> options,
        IImageProcessingService imageProcessing,
        IWebHostEnvironment env,
        ILogger<OpenAiImageGenerationService> logger)
    {
        _http = http;
        _options = options.Value;
        _imageProcessing = imageProcessing;
        _logger = logger;
        _resultsRoot = Path.Combine(env.ContentRootPath, "App_Data", "results");
        Directory.CreateDirectory(_resultsRoot);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<GenerationResult> GenerateEditsAsync(
        string originalPath,
        string maskPath,
        string prompt,
        Guid jobId,
        int variations,
        CancellationToken ct = default)
    {
        variations = Math.Clamp(variations, 1, 4);

        if (!IsConfigured)
        {
            _logger.LogWarning("OpenAI API key missing — using demo mode for job {JobId}", jobId);
            return new GenerationResult(await CreateDemoSetAsync(originalPath, maskPath, prompt, jobId, variations, ct), true);
        }

        try
        {
            var paths = await CallOpenAiEditsAsync(originalPath, maskPath, prompt, jobId, variations, ct);
            return new GenerationResult(paths, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI image edit failed for job {JobId}; falling back to demo mode", jobId);
            return new GenerationResult(await CreateDemoSetAsync(originalPath, maskPath, prompt, jobId, variations, ct), true);
        }
    }

    private async Task<IReadOnlyList<string>> CreateDemoSetAsync(
        string originalPath,
        string maskPath,
        string prompt,
        Guid jobId,
        int variations,
        CancellationToken ct)
    {
        var demoPaths = new List<string>();
        for (var i = 0; i < variations; i++)
        {
            demoPaths.Add(await _imageProcessing.CreateDemoEditAsync(originalPath, maskPath, prompt, jobId, i, ct));
        }

        return demoPaths;
    }

    private async Task<IReadOnlyList<string>> CallOpenAiEditsAsync(
        string originalPath,
        string maskPath,
        string prompt,
        Guid jobId,
        int variations,
        CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        await using var imageStream = File.OpenRead(originalPath);
        await using var maskStream = File.OpenRead(maskPath);

        var imageContent = new StreamContent(imageStream);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "image", "original.png");

        var maskContent = new StreamContent(maskStream);
        maskContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(maskContent, "mask", "mask.png");

        form.Add(new StringContent(BuildConstructionPrompt(prompt)), "prompt");
        form.Add(new StringContent(variations.ToString()), "n");
        form.Add(new StringContent("1024x1024"), "size");
        form.Add(new StringContent("b64_json"), "response_format");

        // dall-e-2 supports edits; keep model configurable for Azure/OpenAI gateways
        if (!string.IsNullOrWhiteSpace(_options.ImageEditModel))
        {
            form.Add(new StringContent(_options.ImageEditModel), "model");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/images/edits")
        {
            Content = form
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI edits failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var resultDir = Path.Combine(_resultsRoot, jobId.ToString("N"));
        Directory.CreateDirectory(resultDir);

        var paths = new List<string>();
        var index = 0;
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            index++;
            var path = Path.Combine(resultDir, $"design-{index}.png");
            if (item.TryGetProperty("b64_json", out var b64))
            {
                var bytes = Convert.FromBase64String(b64.GetString()!);
                await File.WriteAllBytesAsync(path, bytes, ct);
                paths.Add(path);
            }
            else if (item.TryGetProperty("url", out var url))
            {
                var bytes = await _http.GetByteArrayAsync(url.GetString()!, ct);
                await File.WriteAllBytesAsync(path, bytes, ct);
                paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            throw new InvalidOperationException("OpenAI returned no images.");
        }

        return paths;
    }

    private static string BuildConstructionPrompt(string userPrompt)
    {
        return
            "You are an architectural visualization assistant. Edit ONLY the transparent/masked region of this construction photo. " +
            "Preserve every unmasked pixel of the existing building: structure, perspective, lighting, materials, and surroundings. " +
            "Blend the new design seamlessly into the selected area. Photorealistic. Design request: " + userPrompt;
    }
}
