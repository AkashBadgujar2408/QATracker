using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;

namespace QaTracker.Infrastructure.Files;

/// <summary>"Storage" configuration section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for attachment files; relative paths are under the app's content root. Back it up with the database.</summary>
    [Required]
    public string Root { get; set; } = "App_Data/attachments";
}

/// <summary>Files in a local folder. Paths come from the application (never from user input) and must stay inside the root.</summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment environment) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.Root));

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Stream? OpenRead(string path)
    {
        var fullPath = Resolve(path);
        return File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, path));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage path escapes the storage root.");
        }

        return fullPath;
    }
}
