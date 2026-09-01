using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuildVision.Api.Models;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BuildVision.Api.Services;

public interface IDesignSuggestionService
{
    bool IsConfigured { get; }

    Task<DesignSuggestionResponse> SuggestAsync(
        IFormFile image,
        SelectionBox selection,
        string question,
        CancellationToken ct = default);
}

public sealed class DesignSuggestionService : IDesignSuggestionService
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<DesignSuggestionService> _logger;

    public DesignSuggestionService(
        HttpClient http,
        IOptions<OpenAiOptions> options,
        ILogger<DesignSuggestionService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<DesignSuggestionResponse> SuggestAsync(
        IFormFile image,
        SelectionBox selection,
        string question,
        CancellationToken ct = default)
    {
        await using var input = image.OpenReadStream();
        using var img = await Image.LoadAsync<Rgba32>(input, ct);

        var pixelSelection = ToPixelSelection(selection, img.Width, img.Height);
        var region = DescribeRegion(pixelSelection, img.Width, img.Height);

        if (!IsConfigured)
        {
            _logger.LogWarning("OpenAI API key missing — building selection-aware demo suggestions");
            return BuildSelectionAwareSuggestions(question, region, pixelSelection, demo: true);
        }

        try
        {
            var annotatedB64 = EncodeAnnotatedImage(img, pixelSelection);
            var cropB64 = EncodeCrop(img, pixelSelection);
            return await CallVisionAsync(annotatedB64, cropB64, question, region, pixelSelection, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vision suggestion failed; falling back to selection-aware suggestions");
            return BuildSelectionAwareSuggestions(question, region, pixelSelection, demo: true);
        }
    }

    private async Task<DesignSuggestionResponse> CallVisionAsync(
        string annotatedB64,
        string cropB64,
        string question,
        RegionProfile region,
        SelectionBox selection,
        CancellationToken ct)
    {
        var systemPrompt =
            "You are an architectural design advisor for residential/commercial construction photos. " +
            "You receive a full building photo with a RED rectangle marking the user's selected area, " +
            "plus a cropped close-up of that selected area. " +
            "Suggest ONLY designs that fit that selected region and the surrounding structure. " +
            "Do not suggest changes outside the selection. Be practical for real construction. " +
            "Return STRICT JSON only (no markdown) with this shape: " +
            "{\"summary\":\"...\",\"regionInsight\":\"what you see in the selected area\",\"options\":[" +
            "{\"title\":\"Short name\",\"explanation\":\"Why it fits this area\",\"generatePrompt\":\"Detailed image-edit prompt to apply only in the selected region while preserving everything else\"}" +
            "]}. Provide 3 to 4 options.";

        var userText =
            $"User question: {question}\n" +
            $"Selection geometry: x={selection.X:F0}, y={selection.Y:F0}, w={selection.Width:F0}, h={selection.Height:F0} " +
            $"on image {selection.ImageWidth:F0}x{selection.ImageHeight:F0}. " +
            $"Relative placement: horizontal={region.Horizontal}, vertical={region.Vertical}, " +
            $"aspect={region.AspectLabel}, coverage={region.CoveragePercent:F1}% of image. " +
            "Focus suggestions on what can be designed inside the red selection.";

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_options.VisionModel) ? "gpt-4o-mini" : _options.VisionModel,
            temperature = 0.4,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = userText },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:image/png;base64,{annotatedB64}", detail = "high" }
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:image/png;base64,{cropB64}", detail = "high" }
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vision API failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        var parsed = JsonSerializer.Deserialize<VisionJson>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new VisionJson();

        var options = (parsed.Options ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o.Title))
            .Select(o => new DesignSuggestionOption
            {
                Title = o.Title!.Trim(),
                Explanation = string.IsNullOrWhiteSpace(o.Explanation)
                    ? "Fits the selected region and surrounding architecture."
                    : o.Explanation.Trim(),
                GeneratePrompt = string.IsNullOrWhiteSpace(o.GeneratePrompt)
                    ? BuildFallbackGeneratePrompt(o.Title!, question, region)
                    : o.GeneratePrompt.Trim()
            })
            .Take(4)
            .ToList();

        if (options.Count == 0)
        {
            return BuildSelectionAwareSuggestions(question, region, selection, demo: false);
        }

        return new DesignSuggestionResponse
        {
            Summary = string.IsNullOrWhiteSpace(parsed.Summary)
                ? "Based on this selected area and the surrounding structure, here are suitable design options:"
                : parsed.Summary.Trim(),
            RegionInsight = string.IsNullOrWhiteSpace(parsed.RegionInsight)
                ? region.Narrative
                : parsed.RegionInsight.Trim(),
            Options = options,
            UsedDemoMode = false,
            Selection = selection
        };
    }

    private static DesignSuggestionResponse BuildSelectionAwareSuggestions(
        string question,
        RegionProfile region,
        SelectionBox selection,
        bool demo)
    {
        var options = new List<DesignSuggestionOption>();

        if ((region.Vertical is "upper" or "middle") && (region.AspectLabel is "wide" or "square"))
        {
            options.Add(MakeOption(
                "Open Balcony / Veranda",
                $"The selected band sits on the {region.Vertical} elevation and is relatively wide, which suits a projecting balcony with railing that aligns with nearby floor lines.",
                "Add a modern open balcony with slim metal railing and subtle overhang only in the selected region. Match existing white walls and perspective. Keep all unselected structure unchanged."));
        }

        if ((region.AspectLabel is "tall" or "square") && region.Vertical is not "lower")
        {
            options.Add(MakeOption(
                "Window Composition",
                $"A {region.AspectLabel} selection on the {region.Horizontal} facade reads as wall surface — large windows or a window bay would add light and rhythm without fighting the existing massing.",
                "Insert a modern window composition with deep reveals and dark frames only in the selected wall area. Preserve surrounding paint, edges, and roof lines outside the selection."));
        }

        if (region.Vertical == "lower" || region.CoveragePercent > 18)
        {
            options.Add(MakeOption(
                "Terrace Living Deck",
                "The selection covers a broad outdoor plane/wall base typical of unfinished terrace space — shade, flooring, seating, and planters would make it usable and beautiful.",
                "Transform only the selected terrace/open area into an outdoor living deck with pergola shade, warm outdoor flooring, seating, and planters. Do not alter the left balcony railing or other unselected building parts."));
        }

        if (region.AspectLabel == "wide" && (region.Vertical is "middle" or "upper"))
        {
            options.Add(MakeOption(
                "Modern Elevation Feature",
                "A horizontal mid/upper strip can carry cladding, a framed elevation panel, or a recessed architectural feature that upgrades the facade while staying within the selection.",
                "Create a modern elevation feature (wood/metal cladding panel with subtle recess and accent lighting) strictly inside the selected region. Keep neighboring walls, roof, and openings outside the selection unchanged."));
        }

        options.Add(MakeOption(
            "Decorative Wall + Greenery",
            $"Given the plain surface in the {region.Horizontal}/{region.Vertical} selection, textured cladding with climbing plants or vertical greenery softens the elevation and fits practical construction upgrades.",
            "Add decorative wall texture and vertical greenery with soft wall lighting only inside the selected area. Preserve every part of the building outside the mask."));

        if (question.Contains("light", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("evening", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("night", StringComparison.OrdinalIgnoreCase))
        {
            options.Insert(0, MakeOption(
                "Architectural Lighting Accent",
                "Your question emphasizes beauty/ambiance; wall-wash and cove lighting in this selected zone can elevate the facade at dusk without structural rebuild.",
                "Add warm architectural facade lighting, cove accents, and subtle uplights only in the selected region. Keep geometry of unselected areas identical."));
        }

        options = options
            .GroupBy(o => o.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(4)
            .ToList();

        return new DesignSuggestionResponse
        {
            Summary = "Based on this selected area and the surrounding structure, here are some suitable design options:",
            RegionInsight = region.Narrative + (demo
                ? " (Demo analysis from selection geometry — add an OpenAI API key for vision-based reading of materials and details.)"
                : string.Empty),
            Options = options,
            UsedDemoMode = demo,
            Selection = selection
        };
    }

    private static DesignSuggestionOption MakeOption(string title, string explanation, string generatePrompt) => new()
    {
        Title = title,
        Explanation = explanation,
        GeneratePrompt = generatePrompt
    };

    private static string BuildFallbackGeneratePrompt(string title, string question, RegionProfile region) =>
        $"In only the selected region ({region.Horizontal}/{region.Vertical}, {region.AspectLabel}), design: {title}. " +
        $"User intent: {question}. Photorealistic. Preserve all unselected building structure, perspective, and materials.";

    private static SelectionBox ToPixelSelection(SelectionBox selection, int width, int height)
    {
        var scaleX = width / Math.Max(1.0, selection.ImageWidth <= 0 ? width : selection.ImageWidth);
        var scaleY = height / Math.Max(1.0, selection.ImageHeight <= 0 ? height : selection.ImageHeight);
        var x = Math.Clamp(selection.X * scaleX, 0, width - 1);
        var y = Math.Clamp(selection.Y * scaleY, 0, height - 1);
        var w = Math.Clamp(selection.Width * scaleX, 1, width - x);
        var h = Math.Clamp(selection.Height * scaleY, 1, height - y);
        return new SelectionBox
        {
            X = x,
            Y = y,
            Width = w,
            Height = h,
            ImageWidth = width,
            ImageHeight = height
        };
    }

    private static RegionProfile DescribeRegion(SelectionBox s, int width, int height)
    {
        var cx = (s.X + s.Width / 2.0) / width;
        var cy = (s.Y + s.Height / 2.0) / height;
        var aspect = s.Width / Math.Max(1.0, s.Height);
        var coverage = (s.Width * s.Height) / (width * (double)height) * 100.0;

        var horizontal = cx < 0.33 ? "left" : cx > 0.66 ? "right" : "center";
        var vertical = cy < 0.33 ? "upper" : cy > 0.66 ? "lower" : "middle";
        var aspectLabel = aspect > 1.35 ? "wide" : aspect < 0.75 ? "tall" : "square";

        return new RegionProfile(
            horizontal,
            vertical,
            aspectLabel,
            coverage,
            $"Selected a {aspectLabel} region on the {horizontal}-{vertical} portion of the building " +
            $"covering about {coverage:F0}% of the frame.");
    }

    private static string EncodeAnnotatedImage(Image<Rgba32> source, SelectionBox selection)
    {
        using var clone = source.Clone();
        DrawRect(clone, selection, new Rgba32(229, 57, 53, 255), Math.Max(3, clone.Width / 280));
        return ToBase64Png(clone);
    }

    private static void DrawRect(Image<Rgba32> image, SelectionBox selection, Rgba32 color, int thickness)
    {
        var x0 = (int)Math.Floor(selection.X);
        var y0 = (int)Math.Floor(selection.Y);
        var x1 = (int)Math.Min(image.Width - 1, Math.Ceiling(selection.X + selection.Width) - 1);
        var y1 = (int)Math.Min(image.Height - 1, Math.Ceiling(selection.Y + selection.Height) - 1);

        for (var t = 0; t < thickness; t++)
        {
            var left = Math.Min(image.Width - 1, x0 + t);
            var right = Math.Max(0, x1 - t);
            var top = Math.Min(image.Height - 1, y0 + t);
            var bottom = Math.Max(0, y1 - t);
            for (var x = left; x <= right; x++)
            {
                image[x, top] = color;
                image[x, bottom] = color;
            }
            for (var y = top; y <= bottom; y++)
            {
                image[left, y] = color;
                image[right, y] = color;
            }
        }
    }

    private static string EncodeCrop(Image<Rgba32> source, SelectionBox selection)
    {
        var x = (int)Math.Floor(selection.X);
        var y = (int)Math.Floor(selection.Y);
        var w = Math.Max(1, (int)Math.Ceiling(selection.Width));
        var h = Math.Max(1, (int)Math.Ceiling(selection.Height));
        w = Math.Min(w, source.Width - x);
        h = Math.Min(h, source.Height - y);
        using var crop = source.Clone(c => c.Crop(new Rectangle(x, y, w, h)));
        return ToBase64Png(crop);
    }

    private static string ToBase64Png(Image<Rgba32> image)
    {
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private sealed record RegionProfile(
        string Horizontal,
        string Vertical,
        string AspectLabel,
        double CoveragePercent,
        string Narrative);

    private sealed class VisionJson
    {
        public string? Summary { get; set; }
        public string? RegionInsight { get; set; }
        public List<VisionOption>? Options { get; set; }
    }

    private sealed class VisionOption
    {
        public string? Title { get; set; }
        public string? Explanation { get; set; }
        public string? GeneratePrompt { get; set; }
    }
}
