namespace BuildVision.Api.Models;

public sealed class DesignJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProjectName { get; set; } = "Untitled Project";
    public string Prompt { get; set; } = string.Empty;
    public string OriginalImagePath { get; set; } = string.Empty;
    public string? MaskImagePath { get; set; }
    public List<string> ResultImagePaths { get; set; } = [];
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public bool UsedDemoMode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DesignJobDto
{
    public Guid Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string OriginalImageUrl { get; set; } = string.Empty;
    public List<string> ResultImageUrls { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public bool UsedDemoMode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ImageEditModel { get; set; } = "dall-e-2";
    public int DefaultVariations { get; set; } = 2;
}

public sealed class SelectionBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double ImageWidth { get; set; }
    public double ImageHeight { get; set; }
}
