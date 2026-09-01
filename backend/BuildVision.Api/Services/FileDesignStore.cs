using System.Text.Json;
using BuildVision.Api.Models;

namespace BuildVision.Api.Services;

public interface IDesignStore
{
    Task<DesignJob> SaveAsync(DesignJob job, CancellationToken ct = default);
    Task<DesignJob?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DesignJob>> ListAsync(CancellationToken ct = default);
}

public sealed class FileDesignStore : IDesignStore
{
    private readonly string _indexPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileDesignStore(IWebHostEnvironment env)
    {
        var root = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(root);
        _indexPath = Path.Combine(root, "designs.json");
    }

    public async Task<DesignJob> SaveAsync(DesignJob job, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var jobs = await ReadAllAsync(ct);
            var idx = jobs.FindIndex(j => j.Id == job.Id);
            if (idx >= 0)
            {
                jobs[idx] = job;
            }
            else
            {
                jobs.Insert(0, job);
            }

            await File.WriteAllTextAsync(_indexPath, JsonSerializer.Serialize(jobs, JsonOptions), ct);
            return job;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DesignJob?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var jobs = await ReadAllAsync(ct);
        return jobs.FirstOrDefault(j => j.Id == id);
    }

    public async Task<IReadOnlyList<DesignJob>> ListAsync(CancellationToken ct = default)
    {
        return await ReadAllAsync(ct);
    }

    private async Task<List<DesignJob>> ReadAllAsync(CancellationToken ct)
    {
        if (!File.Exists(_indexPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_indexPath);
        return await JsonSerializer.DeserializeAsync<List<DesignJob>>(stream, JsonOptions, ct) ?? [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
