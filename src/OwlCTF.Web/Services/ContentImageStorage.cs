namespace OwlCTF.Services;

public sealed record ContentImageRecord(string FileName, string PublicPath, long SizeBytes, DateTime UploadedAtUtc);

public sealed class ContentImageStorage
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    private const string PublicPrefix = "/uploads/content/";
    private readonly string _root;

    public ContentImageStorage(IWebHostEnvironment environment)
    {
        _root = Path.GetFullPath(Path.Combine(environment.WebRootPath, "uploads", "content"));
        Directory.CreateDirectory(_root);
    }

    public IReadOnlyList<ContentImageRecord> List() => Directory
        .EnumerateFiles(_root, "content-*", SearchOption.TopDirectoryOnly)
        .Select(path => new FileInfo(path))
        .OrderByDescending(file => file.LastWriteTimeUtc)
        .Select(file => new ContentImageRecord(file.Name, PublicPrefix + file.Name, file.Length, file.LastWriteTimeUtc))
        .ToArray();

    public async Task<ContentImageRecord> SaveAsync(IFormFile upload, CancellationToken ct)
    {
        if (upload.Length <= 0 || upload.Length > MaxImageBytes)
            throw new InvalidOperationException("Choose an image up to 5 MB.");

        await using var input = upload.OpenReadStream();
        var header = new byte[12];
        var headerLength = 0;
        while (headerLength < header.Length)
        {
            var read = await input.ReadAsync(header.AsMemory(headerLength, header.Length - headerLength), ct);
            if (read == 0) break;
            headerLength += read;
        }

        var extension = DetectExtension(header.AsSpan(0, headerLength))
            ?? throw new InvalidOperationException("Choose a PNG, JPEG, WebP or GIF image.");
        var name = $"content-{Guid.NewGuid():N}{extension}";
        var path = SafePath(name);

        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await output.WriteAsync(header.AsMemory(0, headerLength), ct);
        var buffer = new byte[81920];
        long total = headerLength;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxImageBytes) throw new InvalidOperationException("Choose an image up to 5 MB.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        catch
        {
            output.Close();
            File.Delete(path);
            throw;
        }

        return new(name, PublicPrefix + name, total, DateTime.UtcNow);
    }

    public bool Delete(string fileName)
    {
        if (!fileName.StartsWith("content-", StringComparison.Ordinal) || fileName != Path.GetFileName(fileName)) return false;
        var path = SafePath(fileName);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private string SafePath(string name)
    {
        if (name != Path.GetFileName(name)) throw new InvalidOperationException("Invalid image path.");
        var path = Path.GetFullPath(Path.Combine(_root, name));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid image path.");
        return path;
    }

    private static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (header.Length >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255) return ".jpg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return ".gif";
        return null;
    }
}
