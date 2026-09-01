using System.Text.Json;
using BuildVision.Api.Models;
using BuildVision.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BuildVision.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DesignsController : ControllerBase
{
    private readonly IDesignStore _store;
    private readonly IImageProcessingService _imageProcessing;
    private readonly IImageGenerationService _generation;
    private readonly IWebHostEnvironment _env;

    public DesignsController(
        IDesignStore store,
        IImageProcessingService imageProcessing,
        IImageGenerationService generation,
        IWebHostEnvironment env)
    {
        _store = store;
        _imageProcessing = imageProcessing;
        _generation = generation;
        _env = env;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DesignJobDto>>> List(CancellationToken ct)
    {
        var jobs = await _store.ListAsync(ct);
        return Ok(jobs.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DesignJobDto>> Get(Guid id, CancellationToken ct)
    {
        var job = await _store.GetAsync(id, ct);
        return job is null ? NotFound() : Ok(ToDto(job));
    }

    [HttpPost("generate")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<DesignJobDto>> Generate(
        [FromForm] IFormFile image,
        [FromForm] string prompt,
        [FromForm] string? projectName,
        [FromForm] string? selectionJson,
        [FromForm] IFormFile? mask,
        [FromForm] int variations = 2,
        CancellationToken ct = default)
    {
        if (image is null || image.Length == 0)
        {
            return BadRequest(new { error = "Image is required." });
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return BadRequest(new { error = "Prompt is required." });
        }

        var selection = ParseSelection(selectionJson);
        var job = new DesignJob
        {
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? "Untitled Project" : projectName.Trim(),
            Prompt = prompt.Trim(),
            Status = "processing",
            UsedDemoMode = !_generation.IsConfigured
        };

        await _store.SaveAsync(job, ct);

        try
        {
            var (originalPath, maskPath) = await _imageProcessing.SaveUploadAndMaskAsync(
                image, selection, mask, job.Id, ct);

            job.OriginalImagePath = originalPath;
            job.MaskImagePath = maskPath;

            var results = await _generation.GenerateEditsAsync(
                originalPath, maskPath, job.Prompt, job.Id, variations, ct);

            job.ResultImagePaths = results.ImagePaths.ToList();
            job.UsedDemoMode = results.UsedDemoMode;
            job.Status = "completed";
            await _store.SaveAsync(job, ct);
            return Ok(ToDto(job));
        }
        catch (Exception ex)
        {
            job.Status = "failed";
            job.Error = ex.Message;
            await _store.SaveAsync(job, ct);
            return StatusCode(500, ToDto(job));
        }
    }

    private DesignJobDto ToDto(DesignJob job) => new()
    {
        Id = job.Id,
        ProjectName = job.ProjectName,
        Prompt = job.Prompt,
        OriginalImageUrl = ToPublicUrl(job.OriginalImagePath),
        ResultImageUrls = job.ResultImagePaths.Select(ToPublicUrl).Where(u => u.Length > 0).ToList(),
        Status = job.Status,
        Error = job.Error,
        UsedDemoMode = job.UsedDemoMode,
        CreatedAt = job.CreatedAt
    };

    private string ToPublicUrl(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !System.IO.File.Exists(absolutePath))
        {
            return string.Empty;
        }

        var root = Path.Combine(_env.ContentRootPath, "App_Data");
        var relative = Path.GetRelativePath(root, absolutePath).Replace('\\', '/');
        return $"/files/{relative}";
    }

    private static SelectionBox? ParseSelection(string? selectionJson)
    {
        if (string.IsNullOrWhiteSpace(selectionJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SelectionBox>(selectionJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}
