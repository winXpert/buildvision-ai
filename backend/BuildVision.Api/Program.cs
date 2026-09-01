using BuildVision.Api.Models;
using BuildVision.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.AddSingleton<IDesignStore, FileDesignStore>();
builder.Services.AddSingleton<IImageProcessingService, ImageProcessingService>();
builder.Services.AddHttpClient<IImageGenerationService, OpenAiImageGenerationService>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200", "https://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("frontend");

var appData = Path.Combine(app.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appData);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(appData),
    RequestPath = "/files"
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.MapGet("/api/health", (IImageGenerationService generation) => Results.Ok(new
{
    status = "ok",
    product = "BuildVision AI",
    aiConfigured = generation.IsConfigured,
    mode = generation.IsConfigured ? "openai" : "demo"
}));

// SPA fallback for Angular client-side routes
app.MapFallbackToFile("index.html");

app.Run();
